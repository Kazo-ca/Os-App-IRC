using System.Text;
using Kazo.Irc.Protocol.Messages;

namespace Kazo.Irc.Protocol.Client;

/// <summary>Registration: CAP negotiation (IRCv3 CAP 302), SASL PLAIN, nickname fallback, welcome and rejoin.</summary>
public sealed partial class IrcClient
{
    /// <summary>SASL sends its payload in pieces of 400 bytes (IRCv3 SASL 3.1).</summary>
    private const int SaslChunkLength = 400;

    /// <summary>The digits tried after the alternates and the underscores when every nickname is taken.</summary>
    private const int NumberedNickCount = 9;

    private readonly Dictionary<string, string> _availableCaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _enabledCaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _nickCandidates = [];
    private string _wantedNick;
    private bool _registered;
    private bool _capNegotiating;
    private int _pendingCapRequests;
    private bool _saslInProgress;
    private int _nickIndex;

    /// <summary>The capabilities the server offers, with their value (<c>sasl=PLAIN,EXTERNAL</c>).</summary>
    public IReadOnlyDictionary<string, string> AvailableCapabilities => _availableCaps;

    /// <summary>The capabilities the server acknowledged.</summary>
    public IReadOnlySet<string> EnabledCapabilities => _enabledCaps;

    /// <summary>True when <paramref name="capability"/> is enabled.</summary>
    public bool HasCapability(string capability) => _enabledCaps.Contains(capability);

    private void ResetRegistration()
    {
        _availableCaps.Clear();
        _enabledCaps.Clear();
        _capNegotiating = false;
        _pendingCapRequests = 0;
        _saslInProgress = false;
        ServerSupport.Reset();
        _nickCandidates.Clear();
        _nickCandidates.Add(_wantedNick);
        _nickCandidates.AddRange(Options.AlternateNicks.Where(n => !string.IsNullOrWhiteSpace(n)));
        _nickCandidates.Add(_wantedNick + "_");
        _nickCandidates.Add(_wantedNick + "__");
        for (var i = 1; i <= NumberedNickCount; i++)
        {
            _nickCandidates.Add(_wantedNick + i);
        }

        _nickIndex = 0;
        CurrentNick = _wantedNick;
    }

    /// <summary>The first lines of a connection: <c>CAP LS 302</c>, <c>PASS</c>, <c>NICK</c>, <c>USER</c>.</summary>
    private async Task BeginRegistrationAsync(CancellationToken cancellationToken)
    {
        _capNegotiating = true;
        await SendAsync(new IrcMessage("CAP", "LS", "302"), cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(Options.ServerPassword))
        {
            await SendAsync(new IrcMessage("PASS", Options.ServerPassword), cancellationToken).ConfigureAwait(false);
        }

        await SendAsync(new IrcMessage("NICK", _nickCandidates[0]), cancellationToken).ConfigureAwait(false);
        var userName = string.IsNullOrWhiteSpace(Options.UserName) ? _wantedNick : Options.UserName;
        await SendAsync(new IrcMessage("USER", userName, "0", "*", Options.RealName), cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleCapAsync(IrcMessage message, CancellationToken cancellationToken)
    {
        var subcommand = message.GetParameter(1)?.ToUpperInvariant();
        var list = message.Parameters.Count > 2 ? message.Parameters[^1] : string.Empty;
        var hasMore = message.Parameters.Count > 3 && message.Parameters[2] == "*";
        switch (subcommand)
        {
            case "LS":
                AddAvailable(list);
                if (!hasMore && _capNegotiating && _pendingCapRequests == 0 && !_saslInProgress)
                {
                    await RequestCapabilitiesAsync(_availableCaps.Keys, cancellationToken).ConfigureAwait(false);
                }

                break;
            case "ACK":
                _pendingCapRequests = Math.Max(0, _pendingCapRequests - 1);
                await AcknowledgeAsync(list, cancellationToken).ConfigureAwait(false);
                break;
            case "NAK":
                _pendingCapRequests = Math.Max(0, _pendingCapRequests - 1);
                await EndCapIfDoneAsync(cancellationToken).ConfigureAwait(false);
                break;
            case "NEW":
                var added = AddAvailable(list);
                await RequestCapabilitiesAsync(added, cancellationToken).ConfigureAwait(false);
                break;
            case "DEL":
                foreach (var name in SplitCaps(list))
                {
                    _availableCaps.Remove(name.Name);
                    _enabledCaps.Remove(name.Name);
                }

                break;
        }
    }

    private List<string> AddAvailable(string list)
    {
        var added = new List<string>();
        foreach (var (name, value) in SplitCaps(list))
        {
            _availableCaps[name] = value;
            added.Add(name);
        }

        return added;
    }

    private static IEnumerable<(string Name, string Value)> SplitCaps(string list)
    {
        foreach (var item in list.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = item.IndexOf('=', StringComparison.Ordinal);
            yield return equals < 0 ? (item, string.Empty) : (item[..equals], item[(equals + 1)..]);
        }
    }

    /// <summary>Asks for the wanted capabilities among <paramref name="offered"/>; ends CAP when there is none to ask during registration.</summary>
    private async Task RequestCapabilitiesAsync(IEnumerable<string> offered, CancellationToken cancellationToken)
    {
        var offeredSet = offered.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wanted = Options.Capabilities
            .Where(c => offeredSet.Contains(c) && !_enabledCaps.Contains(c))
            .Where(c => !c.Equals("sasl", StringComparison.OrdinalIgnoreCase) || CanUseSasl())
            .ToList();
        if (wanted.Count == 0)
        {
            if (Options.Sasl is not null && !_registered && _capNegotiating && !CanUseSasl())
            {
                SaslCompleted?.Invoke(this, new IrcSaslResult(false, "MECHANISM", "The server does not offer SASL PLAIN."));
            }

            await EndCapIfDoneAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        _pendingCapRequests++;
        await SendAsync(new IrcMessage("CAP", "REQ", string.Join(' ', wanted)), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>SASL needs credentials, a registration in progress, and PLAIN among the mechanisms (or no list).</summary>
    private bool CanUseSasl()
    {
        if (Options.Sasl is null || _registered || !_availableCaps.TryGetValue("sasl", out var mechanisms))
        {
            return false;
        }

        return mechanisms.Length == 0
            || mechanisms.Split(',').Contains("PLAIN", StringComparer.OrdinalIgnoreCase);
    }

    private async Task AcknowledgeAsync(string list, CancellationToken cancellationToken)
    {
        var startSasl = false;
        foreach (var (name, _) in SplitCaps(list))
        {
            if (name.StartsWith('-'))
            {
                _enabledCaps.Remove(name[1..]);
                continue;
            }

            _enabledCaps.Add(name);
            startSasl |= name.Equals("sasl", StringComparison.OrdinalIgnoreCase) && CanUseSasl();
        }

        if (startSasl)
        {
            _saslInProgress = true;
            await SendAsync(new IrcMessage("AUTHENTICATE", "PLAIN"), cancellationToken).ConfigureAwait(false);
            return;
        }

        await EndCapIfDoneAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EndCapIfDoneAsync(CancellationToken cancellationToken)
    {
        if (!_capNegotiating || _pendingCapRequests > 0 || _saslInProgress)
        {
            return;
        }

        _capNegotiating = false;
        await SendAsync(new IrcMessage("CAP", "END"), cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>AUTHENTICATE +</c>: the server waits for the PLAIN payload, base64 in pieces of 400.</summary>
    private async Task HandleAuthenticateAsync(IrcMessage message, CancellationToken cancellationToken)
    {
        if (!_saslInProgress || message.GetParameter(0) != "+" || Options.Sasl is not { } sasl)
        {
            return;
        }

        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sasl.Account}\0{sasl.Account}\0{sasl.Password}"));
        foreach (var piece in SplitSaslPayload(payload))
        {
            await SendAsync(new IrcMessage("AUTHENTICATE", piece), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Cuts a base64 payload in pieces of 400; a payload that ends on a full piece is followed by <c>+</c>.</summary>
    internal static IReadOnlyList<string> SplitSaslPayload(string payload)
    {
        var pieces = new List<string>();
        for (var i = 0; i < payload.Length; i += SaslChunkLength)
        {
            pieces.Add(payload.Substring(i, Math.Min(SaslChunkLength, payload.Length - i)));
        }

        if (payload.Length % SaslChunkLength == 0)
        {
            pieces.Add("+");
        }

        return pieces;
    }

    private async Task HandleSaslNumericAsync(IrcMessage message, CancellationToken cancellationToken)
    {
        var text = message.Parameters.Count > 0 ? message.Parameters[^1] : string.Empty;
        switch (message.Command)
        {
            case IrcNumerics.LoggedIn:
                Account = message.GetParameter(2);
                return;
            case IrcNumerics.LoggedOut:
                Account = null;
                return;
            case IrcNumerics.SaslMechanisms:
                return;
            case IrcNumerics.SaslSuccess:
            case IrcNumerics.SaslAlready:
                SaslCompleted?.Invoke(this, new IrcSaslResult(true, message.Command, text));
                break;
            default:
                SaslCompleted?.Invoke(this, new IrcSaslResult(false, message.Command, text));
                break;
        }

        _saslInProgress = false;
        await EndCapIfDoneAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>433</c>, <c>432</c>, <c>436</c>: during registration, try the next nickname; after it, report the error.
    /// When every nickname is taken, the client stops without reconnecting.
    /// </summary>
    private async Task HandleNickRefusedAsync(IrcMessage message, CancellationToken cancellationToken)
    {
        var text = message.Parameters.Count > 0 ? message.Parameters[^1] : string.Empty;
        if (_registered)
        {
            Error?.Invoke(this, new IrcClientError(message.Command, $"{message.GetParameter(1)}: {text}"));
            return;
        }

        _nickIndex++;
        if (_nickIndex >= _nickCandidates.Count)
        {
            Error?.Invoke(this, new IrcClientError("NICK_EXHAUSTED", "Every nickname tried is taken or refused."));
            _stopReconnecting = true;
            await SendAsync(new IrcMessage("QUIT", Options.DefaultQuitMessage), cancellationToken).ConfigureAwait(false);
            return;
        }

        CurrentNick = _nickCandidates[_nickIndex];
        await SendAsync(new IrcMessage("NICK", CurrentNick), cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>001</c>: registered. Join the auto-join channels (first time) and every channel the client was in.</summary>
    private async Task HandleWelcomeAsync(IrcMessage message, CancellationToken cancellationToken)
    {
        _registered = true;
        _capNegotiating = false;
        _saslInProgress = false;
        CurrentNick = message.GetParameter(0) ?? CurrentNick;
        ServerName = message.Source?.Name;
        SetState(IrcClientState.Connected);
        Registered?.Invoke(this, EventArgs.Empty);

        if (!_autoJoinDone)
        {
            _autoJoinDone = true;
            foreach (var join in Options.AutoJoin)
            {
                var channel = GetOrAddChannel(join.Name);
                channel.WantsJoin = true;
                channel.Key ??= join.Key;
            }
        }

        var channels = _channels.Values.Where(c => c.WantsJoin).Select(c => new IrcChannelJoin(c.Name, c.Key)).ToList();
        await SendJoinsAsync(channels, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Joins several channels in as few <c>JOIN</c> lines as the length limit allows (channels with a key first).</summary>
    private async Task SendJoinsAsync(IReadOnlyList<IrcChannelJoin> joins, CancellationToken cancellationToken)
    {
        const int budget = IrcLimits.MaxLineBytes - IrcLimits.LineEndingBytes - 6; // "JOIN " and the space before the keys
        var ordered = joins.OrderBy(j => j.Key is null).ToList();
        var names = new List<string>();
        var keys = new List<string>();
        foreach (var join in ordered)
        {
            var size = string.Join(',', names.Append(join.Name)).Length + string.Join(',', keys.Append(join.Key ?? string.Empty)).Length;
            if (names.Count > 0 && size > budget)
            {
                await SendJoinLineAsync(names, keys, cancellationToken).ConfigureAwait(false);
                names.Clear();
                keys.Clear();
            }

            names.Add(join.Name);
            if (join.Key is not null)
            {
                keys.Add(join.Key);
            }
        }

        if (names.Count > 0)
        {
            await SendJoinLineAsync(names, keys, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task SendJoinLineAsync(List<string> names, List<string> keys, CancellationToken cancellationToken)
    {
        var parameters = new List<string> { string.Join(',', names) };
        if (keys.Count > 0)
        {
            parameters.Add(string.Join(',', keys));
        }

        return SendAsync(new IrcMessage("JOIN", parameters), cancellationToken);
    }
}
