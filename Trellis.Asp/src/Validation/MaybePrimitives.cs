namespace Trellis.Asp.Validation;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

/// <summary>
/// Closed list of STJ-native primitive types recognised by
/// <see cref="MaybePrimitiveJsonConverterFactory"/> and
/// <see cref="Trellis.Asp.ModelBinding.MaybePrimitiveModelBinder{T}"/>. Mirrors the
/// <c>CompositeValueObjectJsonConverter&lt;T&gt;</c> allowed list in <c>Trellis.Primitives</c>.
/// </summary>
/// <remarks>
/// <para>
/// Exposed as a non-generic static holder rather than as a member on
/// <see cref="Trellis.Asp.ModelBinding.MaybePrimitiveModelBinder{T}"/> so the
/// <see cref="FrozenSet{T}"/> is allocated once for the entire framework, not once per closed
/// generic instantiation. Both the JSON converter factory and the model binder provider read
/// from the same set, guaranteeing the wire-shape decision is identical for JSON body binding
/// and route / query / header / form binding.
/// </para>
/// <para>
/// The rule is uniform: <c>Maybe&lt;T&gt;</c> is supported wherever <c>T</c> is a primitive
/// Trellis already supports directly. Shapes outside this list require the wire-shape DTO +
/// adapter pattern at the controller seam (Cookbook Recipe 14).
/// </para>
/// </remarks>
public static class MaybePrimitives
{
    /// <summary>
    /// The closed allowed list of primitive types: <see cref="string"/>, <see cref="decimal"/>,
    /// <see cref="int"/>, <see cref="long"/>, <see cref="short"/>, <see cref="byte"/>,
    /// <see cref="double"/>, <see cref="float"/>, <see cref="bool"/>, <see cref="Guid"/>,
    /// <see cref="DateTime"/>, <see cref="DateTimeOffset"/>.
    /// </summary>
    public static readonly FrozenSet<Type> SupportedPrimitives = new HashSet<Type>
    {
        typeof(string),
        typeof(decimal),
        typeof(int),
        typeof(long),
        typeof(short),
        typeof(byte),
        typeof(double),
        typeof(float),
        typeof(bool),
        typeof(Guid),
        typeof(DateTime),
        typeof(DateTimeOffset),
    }.ToFrozenSet();
}