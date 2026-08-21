namespace Trellis;

using System;

/// <summary>
/// Marks a partial <see cref="RequiredInt{TSelf}"/>, <see cref="RequiredLong{TSelf}"/>, or
/// <see cref="RequiredDecimal{TSelf}"/>-derived class so the source generator rejects any value
/// that is not strictly greater than zero. Equivalent to applying
/// <c>[Range(1, MaxValue)]</c> for integer types and a positive-fractional <c>[Range]</c> for
/// decimal types, but reads as domain intent at the declaration site.
/// </summary>
/// <remarks>
/// <para>
/// Mutually exclusive with <see cref="NonNegativeAttribute"/>, <see cref="NegativeAttribute"/>,
/// and <see cref="NonPositiveAttribute"/> on the same type — the source generator emits a
/// diagnostic when two are combined.
/// </para>
/// <para>
/// Not supported on <see cref="RequiredGuid{TSelf}"/>, <see cref="RequiredDateTime{TSelf}"/>,
/// <see cref="RequiredDateTimeOffset{TSelf}"/>, <see cref="RequiredBool{TSelf}"/>,
/// <see cref="RequiredString{TSelf}"/>, or <see cref="RequiredEnum{TSelf}"/>.
/// </para>
/// </remarks>
/// <seealso cref="NonNegativeAttribute"/>
/// <seealso cref="NegativeAttribute"/>
/// <seealso cref="NonPositiveAttribute"/>
/// <seealso cref="RangeAttribute"/>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PositiveAttribute : Attribute
{
    /// <summary>
    /// Gets or sets an application-defined reason code that replaces the framework default on every
    /// failure this attribute produces.
    /// </summary>
    /// <value>
    /// A non-empty code, or <see langword="null"/> to keep the framework default. Setting it to an
    /// empty or whitespace string is a generator error (<c>TRLS060</c>).
    /// </value>
    /// <remarks>
    /// The framework vocabulary is frozen so that one client catalog entry works across every Trellis
    /// service. An application that owns both ends of its own contract — it wrote the declaration and
    /// it writes the catalog key — may name the failure in its own terms instead, and nothing it does
    /// here can invalidate another party's catalog. An override carries no warning and no penalty.
    /// </remarks>
    public string? Code { get; set; }
}
