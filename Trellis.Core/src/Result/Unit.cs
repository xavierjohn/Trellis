namespace Trellis;

/// <summary>
/// Sentinel type representing the absence of a meaningful value. Used as the type parameter for
/// no-payload results: <c>Result&lt;Unit&gt;</c> represents an operation that either succeeds without
/// producing a value or fails with an <see cref="Error"/>.
/// </summary>
/// <remarks>
/// <para>
/// Trellis v3 (per ADR-005) collapsed the non-generic <c>Result</c> instance type into the static
/// factory class <see cref="Result"/>; the no-payload outcome shape is uniformly <c>Result&lt;Unit&gt;</c>.
/// </para>
/// <para>
/// Use <see cref="Default"/> (or its alias <see cref="Value"/>) when an explicit <see cref="Unit"/>
/// instance is needed. <see cref="Result.Ok()"/> returns <c>Result&lt;Unit&gt;</c> directly without
/// requiring callers to mention <see cref="Unit"/> at all.
/// </para>
/// </remarks>
/// <seealso cref="Result"/>
/// <seealso cref="Result{TValue}"/>
public readonly record struct Unit
{
    /// <summary>The single canonical <see cref="Unit"/> value.</summary>
    public static Unit Default => default;

    /// <summary>Alias for <see cref="Default"/>.</summary>
    public static Unit Value => default;
}