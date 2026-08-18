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
}