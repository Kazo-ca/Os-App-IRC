namespace Kazo.Irc.Protocol.Client;

/// <summary>
/// When the client tries again after a lost connection: a delay that grows at each failed attempt
/// (2 s, 4 s, 8 s... up to 5 minutes by default), back to the first one after a successful
/// registration, as real clients do so a network outage does not flood the server.
/// </summary>
public sealed record IrcReconnectPolicy
{
    /// <summary>A policy that never reconnects.</summary>
    public static IrcReconnectPolicy Disabled { get; } = new() { Enabled = false };

    /// <summary>False: a lost connection stays lost.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The delay before the first attempt.</summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The longest delay.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How much the delay grows at each attempt (at least 1).</summary>
    public double Multiplier { get; init; } = 2;

    /// <summary>The number of attempts in a row before the client gives up, or null to try forever.</summary>
    public int? MaxAttempts { get; init; }

    /// <summary>The delay before attempt <paramref name="attempt"/> (1 for the first one).</summary>
    public TimeSpan GetDelay(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        var factor = Math.Pow(Math.Max(1, Multiplier), attempt - 1);
        var ticks = InitialDelay.Ticks * factor;
        return ticks >= MaxDelay.Ticks ? MaxDelay : TimeSpan.FromTicks((long)ticks);
    }
}
