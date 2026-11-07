using System.Globalization;
using Kazo.Irc.Protocol.Messages;
using Kazo.Irc.Protocol.Text;

namespace Kazo.Irc.Protocol.Client;

/// <summary>PRIVMSG, NOTICE, CTCP and WHOIS.</summary>
public sealed partial class IrcClient
{
    private readonly Dictionary<string, IrcWhoisInfo> _whois = new(StringComparer.Ordinal);
    private IrcSource? _ownSource;

    private async Task HandleChatAsync(IrcMessage message, CancellationToken cancellationToken)
    {
        var target = message.GetParameter(0);
        var text = message.GetParameter(1) ?? string.Empty;
        var source = message.Source;
        var isNotice = message.Command == "NOTICE";
        if (target is null || source is null || source.IsServer || (isNotice && !_registered))
        {
            ServerText?.Invoke(this, new IrcServerText(MessageTime(message), message.Command, text));
            return;
        }

        var channelName = ChannelOf(target);
        var isPrivate = channelName is null;
        var kind = isNotice ? IrcChatMessageKind.Notice : IrcChatMessageKind.Message;
        if (IrcCtcp.TryParse(text, out var ctcp, out var arguments))
        {
            if (ctcp != IrcCtcp.Action)
            {
                await HandleCtcpAsync(message, source, ctcp, arguments, isNotice, cancellationToken).ConfigureAwait(false);
                return;
            }

            kind = IrcChatMessageKind.Action;
            text = arguments;
        }

        var user = TrackUser(source);
        if (message.GetTag(IrcTags.Account) is { } account)
        {
            user.Account = account;
        }

        var conversation = channelName ?? source.Name;
        if (isPrivate && !isNotice)
        {
            OpenQuery(source.Name);
        }

        ChatMessage?.Invoke(this, new IrcChatMessage(
            MessageTime(message), kind, source, target, conversation, text, isPrivate, IsMe(source.Name), message.Tags));
    }

    /// <summary>The channel a target names (<c>#kazo</c>, or <c>@#kazo</c> for the operators of #kazo), or null for a nickname.</summary>
    private string? ChannelOf(string target)
    {
        if (ServerSupport.IsChannel(target))
        {
            return GetChannel(target)?.Name ?? target;
        }

        var status = 0;
        while (status < target.Length && ServerSupport.PrefixSymbols.Contains(target[status], StringComparison.Ordinal))
        {
            status++;
        }

        return status > 0 && ServerSupport.IsChannel(target[status..]) ? GetChannel(target[status..])?.Name ?? target[status..] : null;
    }

    /// <summary>Answers the CTCP queries a real client answers (VERSION, PING, TIME, CLIENTINFO) and shows the rest in the server tab.</summary>
    private async Task HandleCtcpAsync(IrcMessage message, IrcSource source, string command, string arguments, bool isReply, CancellationToken cancellationToken)
    {
        var shown = isReply ? $"CTCP {command} reply from {source.Name}: {arguments}" : $"CTCP {command} from {source.Name}";
        ServerText?.Invoke(this, new IrcServerText(MessageTime(message), "CTCP", shown.TrimEnd(' ', ':')));
        if (isReply)
        {
            return;
        }

        var answer = command switch
        {
            "VERSION" => Options.VersionReply,
            "PING" => arguments,
            "TIME" => _time.GetUtcNow().ToString("R", CultureInfo.InvariantCulture),
            "CLIENTINFO" => "ACTION CLIENTINFO PING TIME VERSION",
            _ => null,
        };
        if (answer is not null)
        {
            await SendAsync(new IrcMessage("NOTICE", source.Name, IrcCtcp.Format(command, answer)), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The WHOIS numerics: gathered per nickname until <c>318</c>. Returns false for another numeric.</summary>
    private bool HandleWhoisNumeric(IrcMessage message)
    {
        var nick = message.GetParameter(1);
        if (nick is null)
        {
            return false;
        }

        var key = ServerSupport.Fold(nick);
        var info = _whois.GetValueOrDefault(key) ?? new IrcWhoisInfo { Nick = nick };
        var last = message.Parameters[^1];
        switch (message.Command)
        {
            case IrcNumerics.WhoisUser:
                info = info with { UserName = message.GetParameter(2), Host = message.GetParameter(3), RealName = last };
                break;
            case IrcNumerics.WhoisServer:
                info = info with { Server = message.GetParameter(2), ServerInfo = last };
                break;
            case IrcNumerics.WhoisOperator:
                info = info with { IsOperator = true };
                break;
            case IrcNumerics.WhoisIdle:
                info = info with { IdleSeconds = ParseLong(message.GetParameter(2)), SignedOnAt = ParseLong(message.GetParameter(3)) is { } on ? DateTimeOffset.FromUnixTimeSeconds(on) : null };
                break;
            case IrcNumerics.WhoisChannels:
                info = info with { Channels = [.. info.Channels, .. last.Split(' ', StringSplitOptions.RemoveEmptyEntries)] };
                break;
            case IrcNumerics.WhoisAccount:
                info = info with { Account = message.GetParameter(2) };
                break;
            case IrcNumerics.Away:
                GetUser(nick)?.AwayMessage = last;
                if (!_whois.ContainsKey(key))
                {
                    RaiseServerText(message); // the answer to a message sent to someone away
                    return true;
                }

                info = info with { AwayMessage = last };
                break;
            case IrcNumerics.NoSuchNick:
                if (!_whois.ContainsKey(key))
                {
                    return false;
                }

                info = info with { Exists = false };
                break;
            case IrcNumerics.EndOfWhois:
                _whois.Remove(key);
                WhoisReceived?.Invoke(this, info);
                return true;
            default:
                return false;
        }

        _whois[key] = info;
        return true;
    }

    private static long? ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
}
