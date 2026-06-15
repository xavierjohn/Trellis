namespace Trellis;

using System;

/// <summary>
/// Opts a <c>Required*</c>-derived value object into per-type sentinel rejection.
/// Without this attribute the bare base accepts every concrete value and rejects only
/// <c>null</c>; with it the generator emits a sentinel-rejection check appropriate to the
/// underlying primitive.
/// </summary>
/// <remarks>
/// <para>
/// The sentinel each base rejects when this attribute is applied:
/// </para>
/// <list type="bullet">
/// <item><see cref="RequiredString{TSelf}"/> — rejects <see cref="string.Empty"/>; combined with <see cref="TrimAttribute"/> a whitespace-only input trims to empty and is rejected.</item>
/// <item><see cref="RequiredGuid{TSelf}"/> — rejects <see cref="Guid.Empty"/>.</item>
/// <item><see cref="RequiredInt{TSelf}"/> / <see cref="RequiredLong{TSelf}"/> / <see cref="RequiredDecimal{TSelf}"/> — rejects <c>0</c>.</item>
/// <item><see cref="RequiredDateTime{TSelf}"/> / <see cref="RequiredDateTimeOffset{TSelf}"/> — rejects <see cref="DateTime.MinValue"/> / <see cref="DateTimeOffset.MinValue"/>.</item>
/// </list>
/// <para>
/// Invalid on <see cref="RequiredBool{TSelf}"/> and <see cref="RequiredEnum{TSelf}"/> — those
/// bases have no meaningful sentinel, so applying it there raises generator error <c>TRLS058</c>.
/// </para>
/// </remarks>
/// <seealso cref="TrimAttribute"/>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NotDefaultAttribute : Attribute
{
}