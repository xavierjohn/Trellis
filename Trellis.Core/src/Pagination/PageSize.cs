namespace Trellis;

using System;

/// <summary>
/// A request/applied limit pair carrying both the limit the client asked for and the
/// limit the server actually applied (after server-side capping). Bridges raw user
/// input directly into <see cref="Page{T}"/>'s <c>RequestedLimit</c> /
/// <c>AppliedLimit</c> invariants so cap visibility (<c>WasCapped</c>) survives
/// end-to-end.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a struct, not a <c>ScalarValueObject&lt;,&gt;</c>?</b> <see cref="PageSize"/> is
/// not a scalar domain value — it is a protocol-level pair (requested versus applied).
/// Modeling it as a scalar would force callers to track <c>Requested</c> separately,
/// defeating the very cap visibility <see cref="Page{T}"/> was designed to surface.
/// </para>
/// <para>
/// <b>Clamping vs. rejecting:</b> <see cref="FromRequested(int?, int)"/> clamps
/// <see cref="Applied"/> down to the server cap while preserving <see cref="Requested"/>
/// verbatim — this is the lenient mode that keeps <see cref="WasCapped"/> observable.
/// <see cref="TryCreate(int?, int, string?)"/> is the strict counterpart that rejects
/// out-of-range requests with <see cref="Error.InvalidInput"/>; use it when the API
/// contract treats over-cap as a client error rather than a soft cap.
/// </para>
/// </remarks>
public readonly record struct PageSize
{
    /// <summary>The default page size used when the client supplies no value.</summary>
    public const int Default = 50;

    /// <summary>The framework-default maximum page size. Callers may override per call via <see cref="FromRequested(int?, int)"/> or <see cref="TryCreate(int?, int, string?)"/>.</summary>
    public const int Max = 100;

    /// <summary>The limit the client requested. Positive for validated instances; <c>default(PageSize)</c> yields zero and should not be observed in well-formed code.</summary>
    public int Requested { get; }

    /// <summary>The limit the server actually applied. Positive for validated instances and never greater than <see cref="Requested"/>; <c>default(PageSize)</c> yields zero.</summary>
    public int Applied { get; }

    /// <summary>True when the server applied a smaller limit than the client requested.</summary>
    public bool WasCapped => Applied < Requested;

    /// <summary>Constructs a validated page size.</summary>
    /// <param name="requested">The limit the client requested. Must be positive.</param>
    /// <param name="applied">The limit the server applied. Must be positive and not greater than <paramref name="requested"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="requested"/> or <paramref name="applied"/> is non-positive, or <paramref name="applied"/> exceeds <paramref name="requested"/>.</exception>
    public PageSize(int requested, int applied)
    {
        if (requested <= 0)
            throw new ArgumentOutOfRangeException(nameof(requested), "Requested limit must be positive.");
        if (applied <= 0)
            throw new ArgumentOutOfRangeException(nameof(applied), "Applied limit must be positive.");
        if (applied > requested)
            throw new ArgumentOutOfRangeException(nameof(applied), "Applied limit cannot exceed requested limit.");

        Requested = requested;
        Applied = applied;
    }

    /// <summary>
    /// Lenient factory: when <paramref name="requested"/> is <c>null</c> or non-positive,
    /// returns a <see cref="PageSize"/> with <see cref="Requested"/> = <see cref="Default"/>
    /// and <see cref="Applied"/> = <c>min(Default, max)</c>. Otherwise preserves
    /// <paramref name="requested"/> verbatim and clamps <see cref="Applied"/> to
    /// <paramref name="max"/>. Cap visibility (<see cref="WasCapped"/>) survives in both branches.
    /// </summary>
    /// <param name="requested">The client-requested limit (e.g. from a query string).</param>
    /// <param name="max">The server-side maximum. Defaults to <see cref="Max"/>.</param>
    /// <returns>A <see cref="PageSize"/> with <c>Requested</c> kept verbatim (or <see cref="Default"/> for null/non-positive input) and <c>Applied = min(Requested, max)</c>.</returns>
    public static PageSize FromRequested(int? requested, int max = Max)
    {
        if (max <= 0)
            throw new ArgumentOutOfRangeException(nameof(max), "Max must be positive.");

        if (requested is null || requested.Value <= 0)
            return new PageSize(Default, Math.Min(Default, max));

        var req = requested.Value;
        var applied = Math.Min(req, max);
        return new PageSize(req, applied);
    }

    /// <summary>
    /// Strict factory: when <paramref name="requested"/> is <c>null</c>, returns a
    /// <see cref="PageSize"/> with <see cref="Requested"/> set to <see cref="Default"/> and
    /// <see cref="Applied"/> clamped to <c>min(Default, max)</c> — the same shape
    /// <see cref="FromRequested(int?, int)"/> uses for the null case. Returns
    /// <see cref="Error.InvalidInput"/> when <paramref name="requested"/> is non-positive
    /// or exceeds <paramref name="max"/>. Use when the API contract treats out-of-range as
    /// a client error rather than a soft cap.
    /// </summary>
    /// <param name="requested">The client-requested limit.</param>
    /// <param name="max">The server-side maximum. Defaults to <see cref="Max"/>.</param>
    /// <param name="fieldName">Optional field name for the error; defaults to <c>"pageSize"</c>.</param>
    public static Result<PageSize> TryCreate(int? requested, int max = Max, string? fieldName = null)
    {
        if (max <= 0)
            throw new ArgumentOutOfRangeException(nameof(max), "Max must be positive.");

        var field = fieldName ?? "pageSize";

        if (requested is null)
            return Result.Ok(new PageSize(Default, Math.Min(Default, max)));

        var req = requested.Value;
        if (req <= 0)
            return Result.Fail<PageSize>(
                Error.InvalidInput.ForField(field, ValidationCodes.PageSizeOutOfRange, $"{field} must be positive."));
        if (req > max)
            return Result.Fail<PageSize>(
                Error.InvalidInput.ForField(field, ValidationCodes.PageSizeOutOfRange, $"{field} must be at most {max}."));

        return Result.Ok(new PageSize(req, req));
    }
}