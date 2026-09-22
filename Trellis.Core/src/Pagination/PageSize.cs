namespace Trellis;

/// <summary>
/// Validated requested/applied limits. Missing input uses the default; non-positive input fails;
/// above-cap input is clamped unless rejection is explicitly selected.
/// </summary>
public sealed record PageSize
{
    /// <summary>Default limit when no size is supplied.</summary>
    public const int Default = 50;

    /// <summary>Default server cap.</summary>
    public const int Max = 100;

    /// <summary>Largest limit for which over-fetching one additional row cannot overflow.</summary>
    public const int MaxApplied = int.MaxValue - 1;

    /// <summary>The requested limit, or the configured default when absent.</summary>
    public int Requested { get; }

    /// <summary>The positive applied limit, no greater than Requested or MaxApplied.</summary>
    public int Applied { get; }

    /// <summary>Whether the server capped the requested size.</summary>
    public bool WasCapped => Applied < Requested;

    /// <summary>Constructs validated limits from trusted values; invalid arguments throw.</summary>
    public PageSize(int requested, int applied)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requested);
        if (applied is <= 0 or > MaxApplied || applied > requested)
            throw new ArgumentOutOfRangeException(nameof(applied), "Applied must be positive, no greater than Requested, and allow over-fetching.");
        Requested = requested;
        Applied = applied;
    }

    /// <summary>
    /// Creates a size from trusted input, defaulting absence and clamping above-cap values.
    /// Throws for non-positive input. Use TryCreate at an untrusted boundary.
    /// </summary>
    public static PageSize FromRequested(int? requested, int max = Max)
    {
        var result = TryCreate(requested, max);
        if (!result.TryGetValue(out var size))
            throw new ArgumentOutOfRangeException(nameof(requested), "Requested limit must be positive.");
        return size;
    }

    /// <summary>
    /// Parses client input using an explicit cap policy. Invalid client input returns InvalidInput;
    /// invalid server maximum, default or policy throws.
    /// </summary>
    public static Result<PageSize> TryCreate(
        int? requested,
        int max = Max,
        string? fieldName = null,
        PageSizeLimitPolicy policy = PageSizeLimitPolicy.Clamp,
        int defaultSize = Default)
    {
        if (max is <= 0 or > MaxApplied)
            throw new ArgumentOutOfRangeException(nameof(max), "Max must allow over-fetching one additional row.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defaultSize);
        if (policy is not PageSizeLimitPolicy.Clamp and not PageSizeLimitPolicy.Reject)
            throw new ArgumentOutOfRangeException(nameof(policy));

        var field = fieldName ?? "pageSize";
        if (requested is null)
            return Result.Ok(new PageSize(defaultSize, Math.Min(defaultSize, max)));
        var req = requested.Value;
        if (req <= 0)
            return Result.Fail<PageSize>(Error.InvalidInput.ForField(
                field: field, code: ValidationCodes.PageSizeOutOfRange, args: ValidationArgs.Of("comparisonValue", 1), detail: $"{field} must be positive."));
        if (req > max && policy == PageSizeLimitPolicy.Reject)
            return Result.Fail<PageSize>(Error.InvalidInput.ForField(
                field: field, code: ValidationCodes.PageSizeOutOfRange, args: ValidationArgs.Of("comparisonValue", max), detail: $"{field} must be at most {max}."));
        return Result.Ok(new PageSize(req, Math.Min(req, max)));
    }
}

/// <summary>Policy for a requested limit above the server maximum.</summary>
public enum PageSizeLimitPolicy
{
    /// <summary>Preserve the requested limit and cap the applied limit.</summary>
    Clamp,
    /// <summary>Reject an explicitly requested limit above the maximum.</summary>
    Reject
}