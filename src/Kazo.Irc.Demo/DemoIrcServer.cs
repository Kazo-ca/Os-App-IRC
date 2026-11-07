using System.Globalization;
using System.Threading.Channels;
using Kazo.Irc.Protocol.Messages;
using Kazo.Irc.Protocol.Transport;

namespace Kazo.Irc.Demo;

/// <summary>
/// A small IRC server in memory, seen by the client as its transport: it registers the client
/// (CAP, NICK, USER, welcome numerics, MOTD), answers <c>PING</c>, <c>JOIN</c>, <c>PART</c>,
/// <c>PRIVMSG</c>, <c>TOPIC</c>, <c>WHOIS</c>, <c>AWAY</c>, <c>NICK</c> and <c>QUIT</c>, and has a
/// bot, <c>kazo-bot</c>, that talks back so the chat, the mentions and the private messages can be
/// tried with no network.
/// </summary>
/// <remarks>
/// <para>What the bot does (also written in the MOTD):</para>
/// <list type="bullet">
/// <item>a channel message that names it (<c>kazo-bot: hello</c>): it answers in the channel and names the sender (a mention);</item>
/// <item><c>!mention N</c> in a channel: N seconds later (3 by default, 60 at most) it names the sender in that channel;</item>
/// <item><c>!private N</c>: N seconds later it sends the sender a private message;</item>
/// <item>a private message to it: it answers privately.</item>
/// </list>
/// <para>
/// Every connection attempt of the client gets a new instance (a new "connection"); the server's
/// state (topics, the users of the channels) is per instance. Thread-safe: the client reads on its
/// loop while the delayed answers of the bot are written from timers.
/// </para>
/// </remarks>
public sealed class DemoIrcServer : IIrcTransport
{
    /// <summary>The server's name, in the source of its numerics.</summary>
    public const string ServerName = "demo.kazo.local";

    /// <summary>The network's name (ISUPPORT <c>NETWORK</c>).</summary>
    public const string NetworkName = "KazoDemo";

    /// <summary>The bot's nickname.</summary>
    public const string BotNick = "kazo-bot";

    /// <summary>The channel the demonstration suggests.</summary>
    public const string MainChannel = "#kazo";

    /// <summary>The wait of <c>!mention</c> and <c>!private</c> without a number.</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(3);

    /// <summary>The longest wait of <c>!mention</c> and <c>!private</c>.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    /// <summary>
    /// What the bot answers to <c>!links</c> (work item 5): a web address of the simulated network
    /// between brackets and before a full stop, IRC formatting codes (bold, a mIRC color, italics)
    /// and text that looks like HTML, which must stay text.
    /// </summary>
    public const string LinksMessage =
        "\u0002Links\u0002: the docs (http://docs.hack/guide?topic=irc), in \u000304,01red\u0003 and \u001Ditalics\u001D. "
        + "<b>Not bold</b> <script>alert(1)</script> https://kazo.ca/.";

    private static readonly string[] OtherUsers = ["morpheus", "trinity"];

    /// <summary>The message of the day: what to try.</summary>
    private static readonly string[] Motd =
    [
        "This is a demonstration network: nothing leaves the app.",
        $"/join {MainChannel} then say hello.",
        $"{BotNick}: hello  -> the bot answers and names you.",
        "!mention 5  -> the bot names you in 5 seconds (switch to another window to see the notification).",
        "!private 5  -> the bot sends you a private message in 5 seconds.",
        "!links  -> the bot sends web addresses and formatted text.",
        $"/msg {BotNick} hi  -> a private conversation with the bot.",
    ];

    private readonly Channel<string?> _toClient = Channel.CreateUnbounded<string?>();
    private readonly Dictionary<string, DemoChannel> _channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private string? _nick;
    private bool _user;
    private bool _capNegotiating;
    private bool _registered;
    private bool _closed;

    /// <summary>Creates a server; its clock times the bot's delayed answers.</summary>
    /// <param name="timeProvider">The clock; the system clock by default.</param>
    public DemoIrcServer(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _channels[MainChannel] = new DemoChannel(MainChannel, "Welcome to the Kazo OS IRC demonstration network");
        _channels["#help"] = new DemoChannel("#help", "Ask kazo-bot: type kazo-bot: help");
    }

    /// <summary>The client's nickname, once given.</summary>
    public string? ClientNick
    {
        get
        {
            lock (_gate)
            {
                return _nick;
            }
        }
    }

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _toClient.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IrcMessage.TryParse(line, out var message) || message is null)
        {
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            if (_closed)
            {
                throw new IOException("The demonstration connection is closed.");
            }

            Handle(message);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CloseAsync(CancellationToken cancellationToken)
    {
        Close();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Close();
        return ValueTask.CompletedTask;
    }

    /// <summary>Reads <c>!command</c> or <c>!command N</c>; the delay is clamped to 0..60 s.</summary>
    /// <param name="text">The message.</param>
    /// <param name="command">The command, with its <c>!</c>.</param>
    /// <param name="delay">The delay asked for, or <see cref="DefaultDelay"/>.</param>
    /// <returns>True when the message is that command.</returns>
    public static bool TryReadCommand(string text, string command, out TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(text);
        delay = DefaultDelay;
        var trimmed = text.Trim();
        if (!trimmed.StartsWith(command, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = trimmed[command.Length..];
        if (rest.Length == 0)
        {
            return true;
        }

        if (!char.IsWhiteSpace(rest[0]))
        {
            return false;
        }

        if (int.TryParse(rest.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            delay = TimeSpan.FromSeconds(Math.Min(seconds, MaxDelay.TotalSeconds));
        }

        return true;
    }

    private void Close()
    {
        lock (_gate)
        {
            if (_closed && _lifetime.IsCancellationRequested)
            {
                return;
            }

            _closed = true;
            _toClient.Writer.TryComplete();
        }

        _lifetime.Cancel();
    }

    // Every handler below runs with _gate held.
    private void Handle(IrcMessage message)
    {
        switch (message.Command)
        {
            case "CAP":
                HandleCap(message);
                break;
            case "PASS":
            case "PONG":
                break;
            case "NICK":
                HandleNick(message.GetParameter(0));
                break;
            case "USER":
                _user = true;
                TryWelcome();
                break;
            case "PING":
                Send($":{ServerName} PONG {ServerName} :{message.GetParameter(0)}");
                break;
            default:
                if (_registered)
                {
                    HandleRegistered(message);
                }
                else
                {
                    Numeric("451", "You have not registered");
                }

                break;
        }
    }

    private void HandleRegistered(IrcMessage message)
    {
        switch (message.Command)
        {
            case "JOIN":
                ForEachChannel(message.GetParameter(0), Join);
                break;
            case "PART":
                ForEachChannel(message.GetParameter(0), name => Part(name, message.GetParameter(1)));
                break;
            case "PRIVMSG":
                Privmsg(message.GetParameter(0), message.GetParameter(1) ?? string.Empty);
                break;
            case "NOTICE":
                break;
            case "TOPIC":
                Topic(message.GetParameter(0), message.Parameters.Count > 1 ? message.GetParameter(1) : null);
                break;
            case "WHOIS":
                Whois(message.GetParameter(message.Parameters.Count - 1) ?? string.Empty);
                break;
            case "AWAY":
                var away = !string.IsNullOrEmpty(message.GetParameter(0));
                Numeric(away ? "306" : "305", away ? "You have been marked as being away" : "You are no longer marked as being away");
                break;
            case "MODE":
                if (message.GetParameter(0) is { } target && _channels.TryGetValue(target, out var channel))
                {
                    Numeric("324", channel.Name, "+nt");
                }

                break;
            case "QUIT":
                Send($"ERROR :Closing link: {_nick} (Quit: {message.GetParameter(0) ?? "Client quit"})");
                _closed = true;
                _toClient.Writer.TryComplete();
                break;
            default:
                Numeric("421", message.Command, "Unknown command");
                break;
        }
    }

    private static void ForEachChannel(string? list, Action<string> action)
    {
        foreach (var name in (list ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            action(name);
        }
    }

    private void HandleCap(IrcMessage message)
    {
        switch (message.GetParameter(0)?.ToUpperInvariant())
        {
            case "LS":
                _capNegotiating = true;
                Send($":{ServerName} CAP * LS :multi-prefix server-time");
                break;
            case "REQ":
                Send($":{ServerName} CAP * ACK :{message.GetParameter(1)}");
                break;
            case "END":
                _capNegotiating = false;
                TryWelcome();
                break;
        }
    }

    private void HandleNick(string? nick)
    {
        if (string.IsNullOrEmpty(nick))
        {
            Numeric("431", "No nickname given");
            return;
        }

        if (string.Equals(nick, BotNick, StringComparison.OrdinalIgnoreCase)
            || OtherUsers.Contains(nick, StringComparer.OrdinalIgnoreCase))
        {
            Numeric("433", nick, "Nickname is already in use");
            return;
        }

        if (_registered)
        {
            Send($":{Source(_nick!)} NICK :{nick}");
            foreach (var channel in _channels.Values)
            {
                var index = channel.Users.FindIndex(user => string.Equals(user, _nick, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    channel.Users[index] = nick;
                }
            }
        }

        _nick = nick;
        TryWelcome();
    }

    private void TryWelcome()
    {
        if (_registered || _nick is null || !_user || _capNegotiating)
        {
            return;
        }

        _registered = true;
        Numeric("001", $"Welcome to the {NetworkName} network, {_nick}");
        Numeric("002", $"Your host is {ServerName}, running the Kazo OS demonstration server");
        Numeric("003", "This server lives inside the IRC app, in memory");
        Numeric("004", ServerName, "kazo-demo-1", "iow", "ntkov");
        Numeric("005", $"NETWORK={NetworkName}", "CHANTYPES=#", "PREFIX=(ov)@+", "CASEMAPPING=rfc1459", "CHANMODES=b,k,l,nt", "are supported by this server");
        Numeric("375", $"- {ServerName} Message of the day -");
        foreach (var line in Motd)
        {
            Numeric("372", "- " + line);
        }

        Numeric("376", "End of /MOTD command.");
    }

    private void Join(string name)
    {
        if (!name.StartsWith('#'))
        {
            Numeric("403", name, "No such channel");
            return;
        }

        if (!_channels.TryGetValue(name, out var channel))
        {
            channel = new DemoChannel(name, null);
            _channels[name] = channel;
        }

        if (channel.Users.Contains(_nick!, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        channel.Users.Add(_nick!);
        Send($":{Source(_nick!)} JOIN {channel.Name}");
        SendTopic(channel);
        var names = string.Join(' ', channel.Users.Select(user => user switch
        {
            BotNick => "@" + user,
            "morpheus" => "+" + user,
            _ => user,
        }));
        Numeric("353", "=", channel.Name, names);
        Numeric("366", channel.Name, "End of /NAMES list.");
    }

    private void SendTopic(DemoChannel channel)
    {
        if (channel.Topic is null)
        {
            Numeric("331", channel.Name, "No topic is set");
        }
        else
        {
            Numeric("332", channel.Name, channel.Topic);
        }
    }

    private void Part(string name, string? reason)
    {
        if (!_channels.TryGetValue(name, out var channel)
            || channel.Users.RemoveAll(user => string.Equals(user, _nick, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            Numeric("442", name, "You're not on that channel");
            return;
        }

        Send(reason is null ? $":{Source(_nick!)} PART {channel.Name}" : $":{Source(_nick!)} PART {channel.Name} :{reason}");
    }

    private void Topic(string? name, string? text)
    {
        if (name is null || !_channels.TryGetValue(name, out var channel))
        {
            Numeric("403", name ?? "*", "No such channel");
            return;
        }

        if (text is null)
        {
            SendTopic(channel);
            return;
        }

        channel.Topic = text;
        Send($":{Source(_nick!)} TOPIC {channel.Name} :{text}");
    }

    private void Whois(string nick)
    {
        var known = string.Equals(nick, _nick, StringComparison.OrdinalIgnoreCase)
            || string.Equals(nick, BotNick, StringComparison.OrdinalIgnoreCase)
            || OtherUsers.Contains(nick, StringComparer.OrdinalIgnoreCase);
        if (known)
        {
            Numeric("311", nick, nick == BotNick ? "bot" : "user", "demo.kazo.local", "*", nick == BotNick ? "The demonstration bot" : "A demonstration user");
            Numeric("312", nick, ServerName, "Kazo OS demonstration server");
        }
        else
        {
            Numeric("401", nick, "No such nick/channel");
        }

        Numeric("318", nick, "End of /WHOIS list.");
    }

    private void Privmsg(string? target, string text)
    {
        if (string.IsNullOrEmpty(target))
        {
            Numeric("411", "No recipient given (PRIVMSG)");
            return;
        }

        var nick = _nick!;
        if (!target.StartsWith('#'))
        {
            if (string.Equals(target, BotNick, StringComparison.OrdinalIgnoreCase))
            {
                Send($":{Source(BotNick)} PRIVMSG {nick} :You said: {text}");
            }
            else if (!OtherUsers.Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                Numeric("401", target, "No such nick/channel");
            }

            return;
        }

        if (!_channels.TryGetValue(target, out var channel) || !channel.Users.Contains(nick, StringComparer.OrdinalIgnoreCase))
        {
            Numeric("404", target, "Cannot send to channel");
            return;
        }

        if (string.Equals(text.Trim(), "!links", StringComparison.OrdinalIgnoreCase))
        {
            Send($":{Source(BotNick)} PRIVMSG {channel.Name} :{LinksMessage}");
        }
        else if (TryReadCommand(text, "!mention", out var mentionDelay))
        {
            Later(mentionDelay, $":{Source(BotNick)} PRIVMSG {channel.Name} :{nick}: you asked me to name you, here you are.");
        }
        else if (TryReadCommand(text, "!private", out var privateDelay))
        {
            Later(privateDelay, $":{Source(BotNick)} PRIVMSG {nick} :Hello {nick}, this is a private message.");
        }
        else if (text.Contains(BotNick, StringComparison.OrdinalIgnoreCase))
        {
            Send($":{Source(BotNick)} PRIVMSG {channel.Name} :{nick}: hello! Try !mention 5 or !private 5.");
        }
    }

    private void Later(TimeSpan delay, string line)
    {
        var token = _lifetime.Token;
        _ = LaterAsync();

        async Task LaterAsync()
        {
            try
            {
                await Task.Delay(delay, _time, token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (!_closed)
                    {
                        Send(line);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The connection closed before the bot answered.
            }
        }
    }

    private static string Source(string nick) => $"{nick}!{(nick == BotNick ? "bot" : "user")}@demo.kazo.local";

    private void Numeric(string numeric, params string[] parameters)
    {
        var all = new List<string>(parameters.Length + 1) { _nick ?? "*" };
        all.AddRange(parameters);
        Send(new IrcMessage(numeric, all, new IrcSource(ServerName)).Format());
    }

    private void Send(string line) => _toClient.Writer.TryWrite(line);

    /// <summary>A channel of the demonstration server.</summary>
    private sealed class DemoChannel(string name, string? topic)
    {
        public string Name { get; } = name;

        public string? Topic { get; set; } = topic;

        public List<string> Users { get; } = [BotNick, .. OtherUsers];
    }
}
