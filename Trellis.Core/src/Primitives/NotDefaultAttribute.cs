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
