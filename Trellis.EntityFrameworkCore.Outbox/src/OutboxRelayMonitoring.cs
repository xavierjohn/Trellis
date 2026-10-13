namespace Trellis.EntityFrameworkCore;

/// <summary>Whether outbox readiness evaluates observations from the relay in this process.</summary>
public enum OutboxRelayMonitoring
{
    /// <summary>Require and monitor the local outbox relay.</summary>
    LocalRelay,

    /// <summary>Evaluate only queue metadata; no local or remote relay health is observed.</summary>
    Disabled
}