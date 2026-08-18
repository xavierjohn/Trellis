namespace Trellis.Asp.Validation;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trellis;

/// <summary>
/// Factory for <see cref="MaybePrimitiveJsonConverter{T}"/> covering <see cref="Maybe{T}"/>
/// where <c>T</c> is an STJ-native primitive (<c>string</c>, <c>decimal</c>, <c>int</c>,
/// <c>long</c>, <c>short</c>, <c>byte</c>, <c>double</c>, <c>float</c>, <c>bool</c>,
/// <see cref="Guid"/>, <see cref="DateTime"/>, <see cref="DateTimeOffset"/>).
/// </summary>
/// <remarks>
/// <para>
/// Closes the asymmetry where <see cref="MaybeScalarValueJsonConverterFactory"/> already
/// supports <c>Maybe&lt;TScalar&gt;</c> (typed value objects) but raw <c>Maybe&lt;long&gt;</c>
/// / <c>Maybe&lt;string&gt;</c> / etc. fell through to STJ's default object handling, which
/// silently serializes the <see cref="Maybe{T}"/> struct's public members (<c>HasValue</c>,
/// <c>HasNoValue</c>, <c>Value</c>) — producing JSON the converter cannot itself parse back.
/// The supported primitive set deliberately mirrors the
/// <c>CompositeValueObjectJsonConverter&lt;T&gt;</c> allowed list (in <c>Trellis.Primitives</c>):
/// the rule is "Maybe&lt;T&gt; works wherever T is a primitive Trellis already supports directly".
/// </para>
/// <para>
/// Wire shape: JSON <c>null</c> or absent property reads as <see cref="Maybe{T}.None"/>;
/// a primitive value reads as <see cref="Maybe.From{T}(T)"/>. <see cref="Maybe{T}.None"/>
/// writes as JSON <c>null</c>. STJ-native primitive parsing rules apply for the underlying
/// value (formatting errors throw the standard <see cref="JsonException"/> with a field path).
/// </para>
/// <para>
/// Registered by <c>AddScalarValueValidation()</c> (or by the convenience helper
/// <c>AddTrellisAspWithScalarValidation()</c>) alongside the existing scalar factory.
/// </para>
/// </remarks>
public sealed class MaybePrimitiveJsonConverterFactory : JsonConverterFactory
{
    /// <summary>
    /// True when <paramref name="typeToConvert"/> is <c>Maybe&lt;T&gt;</c> and <c>T</c> is in
    /// the closed primitive allowed list defined by <see cref="MaybePrimitives.SupportedPrimitives"/>.
    /// </summary>
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        if (!typeToConvert.IsGenericType)
            return false;
        if (typeToConvert.GetGenericTypeDefinition() != typeof(Maybe<>))
            return false;
        var inner = typeToConvert.GetGenericArguments()[0];
        return MaybePrimitives.SupportedPrimitives.Contains(inner);
    }

    /// <summary>Creates a <see cref="MaybePrimitiveJsonConverter{T}"/> for the supplied <c>Maybe&lt;T&gt;</c>.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2055", Justification = "T is a closed-set primitive (string/decimal/int/long/short/byte/double/float/bool/Guid/DateTime/DateTimeOffset); the generic instantiation is trim-safe by construction.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "MakeGenericType over a closed primitive set. Trellis registers this factory only when JsonSerializer.IsReflectionEnabledByDefault is true (same gate as the existing scalar factory).")]
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        if (!CanConvert(typeToConvert))
            return null;
        var inner = typeToConvert.GetGenericArguments()[0];
        return (JsonConverter?)Activator.CreateInstance(
            typeof(MaybePrimitiveJsonConverter<>).MakeGenericType(inner));
    }
}