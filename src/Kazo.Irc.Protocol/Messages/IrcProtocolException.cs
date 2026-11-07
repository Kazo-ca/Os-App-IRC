namespace Kazo.Irc.Protocol.Messages;

/// <summary>A line that cannot be read or written as an IRC message.</summary>
public class IrcProtocolException : Exception
{
    /// <summary>Creates the exception with its message.</summary>
    public IrcProtocolException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with its message and its cause.</summary>
    public IrcProtocolException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>A message longer than the IRC limits (<see cref="IrcLimits"/>) once written.</summary>
public sealed class IrcMessageTooLongException : IrcProtocolException
{
    /// <summary>Creates the exception for a part that holds <paramref name="actualBytes"/> bytes out of <paramref name="maxBytes"/>.</summary>
    public IrcMessageTooLongException(string part, int actualBytes, int maxBytes)
        : base($"The {part} of the IRC message holds {actualBytes} bytes; the limit is {maxBytes}.")
    {
        Part = part;
        ActualBytes = actualBytes;
        MaxBytes = maxBytes;
    }

    /// <summary>"message" or "tags".</summary>
    public string Part { get; }

    /// <summary>Bytes the part would hold.</summary>
    public int ActualBytes { get; }

    /// <summary>Bytes allowed.</summary>
    public int MaxBytes { get; }
}
