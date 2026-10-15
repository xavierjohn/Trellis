namespace Trellis.EntityFrameworkCore;

/// <summary>Lag measurements an application-owned provider can supply to outbox readiness.</summary>
[Flags]
public enum ConsumerLagMeasures
{
    /// <summary>Consumer lag is disabled.</summary>
    None = 0,

    /// <summary>Evaluate the age of the oldest durably unaccounted source message.</summary>
    Age = 1,

    /// <summary>Evaluate the number of durably unaccounted source messages.</summary>
    Count = 2,
}