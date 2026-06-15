namespace Trellis;

using System;

/// <summary>
/// Opts a <see cref="RequiredString{TSelf}"/>-derived value object into trim-before-validate.
/// Without this attribute the input string is stored verbatim; with it the generator trims
/// the input before any other check, so combined with <see cref="NotDefaultAttribute"/> a
/// whitespace-only input trims to <see cref="string.Empty"/> and is rejected.
/// </summary>
/// <remarks>
/// Only valid on <see cref="RequiredString{TSelf}"/>-derived types; applying it to any other
/// Required base raises generator error <c>TRLS057</c>. Applying <c>[Trim, NotDefault]</c>
/// together is the recommended setup for any string mapped to a database column and recovers
/// the legacy "reject null + empty + whitespace; auto-trim" behavior.
/// </remarks>
/// <seealso cref="NotDefaultAttribute"/>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TrimAttribute : Attribute
{
}