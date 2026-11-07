namespace Kazo.Irc.Protocol.Client;

/// <summary>Where the client is in the life of its connection.</summary>
public enum IrcClientState
{
    /// <summary>Not connected, and not trying to.</summary>
    Disconnected,

    /// <summary>The transport is opening.</summary>
    Connecting,

    /// <summary>Connected; capabilities, SASL and the nickname are being negotiated.</summary>
    Registering,

    /// <summary>Registered (<c>001</c> received): the client can talk.</summary>
    Connected,

    /// <summary>The connection was lost; the client waits before the next attempt.</summary>
    Reconnecting,
}
