using Kazo.Irc.Protocol.Messages;
using Kazo.Irc.Protocol.Model;

namespace Kazo.Irc.Protocol.Client;

/// <summary>Channel modes: <c>MODE #channel +ov-k nick nick key</c> and the <c>324</c> reply.</summary>
public sealed partial class IrcClient
{
    private void HandleMode(IrcMessage message)
    {
        var target = message.GetParameter(0);
        if (target is null || !ServerSupport.IsChannel(target) || GetChannel(target) is not { } channel)
        {
            if (target is not null && IsMe(target))
            {
                RaiseServerText(message); // the client's own user modes
            }

            return;
        }

        var changes = message.Parameters.Skip(1).ToList();
        ApplyModes(channel, changes);
        var nick = message.Source?.Name ?? string.Empty;
        RaiseActivity(message, IrcActivityKind.Mode, channel.Name, nick, string.Join(' ', changes), own: IsMe(nick));
    }

    private void ApplyChannelModeIs(IrcMessage message)
    {
        if (message.GetParameter(1) is not { } name || GetChannel(name) is not { } channel)
        {
            return;
        }

        channel.ClearModes();
        ApplyModes(channel, message.Parameters.Skip(2).ToList());
    }

    /// <summary>
    /// Reads a mode string and its parameters, with the parameter rules of ISUPPORT: prefix modes
    /// (<c>o</c>, <c>v</c>) and types A and B always take one, type C only when set, type D never.
    /// </summary>
    internal void ApplyModes(IrcChannel channel, IReadOnlyList<string> changes)
    {
        if (changes.Count == 0)
        {
            return;
        }

        var next = 1;
        var adding = true;
        foreach (var mode in changes[0])
        {
            if (mode is '+' or '-')
            {
                adding = mode == '+';
                continue;
            }

            string? parameter = null;
            if (TakesParameter(mode, adding) && next < changes.Count)
            {
                parameter = changes[next++];
            }

            if (ServerSupport.PrefixModes.Contains(mode, StringComparison.Ordinal))
            {
                if (parameter is not null)
                {
                    channel.SetUserMode(parameter, mode, adding);
                }
            }
            else if (!ServerSupport.ListModes.Contains(mode, StringComparison.Ordinal))
            {
                channel.SetMode(mode, parameter, adding);
            }
        }
    }

    private bool TakesParameter(char mode, bool adding) =>
        ServerSupport.PrefixModes.Contains(mode, StringComparison.Ordinal)
        || ServerSupport.ListModes.Contains(mode, StringComparison.Ordinal)
        || ServerSupport.AlwaysParameterModes.Contains(mode, StringComparison.Ordinal)
        || (adding && ServerSupport.SetParameterModes.Contains(mode, StringComparison.Ordinal));
}
