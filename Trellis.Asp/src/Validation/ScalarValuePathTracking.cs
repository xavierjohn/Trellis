namespace Trellis.Asp.Validation;

using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Registry of compile-time-closed validation and path-tracking converters, keyed by the value or container type
/// they wrap. It is the Native AOT counterpart to the reflection pipeline's runtime
/// <c>Type.MakeGenericType</c> construction: AOT forbids building a closed generic at runtime, so the
/// closed instantiations are created by the source generator at compile time and registered here.
/// </summary>
/// <remarks>
/// <para>
/// Consumers do not normally call this type. The Trellis ASP source generator emits a
/// <c>[ModuleInitializer]</c> that registers every scalar property and container type in the DTO graph
/// reachable from a <c>[JsonSerializable]</c>-annotated <c>JsonSerializerContext</c>, so index-precise
/// validation field paths (<c>/members/0/email</c>) work identically in reflection and Native AOT.
/// </para>
/// <para>
/// Manual registration is supported for the cases the generator cannot see — for example a DTO reached
/// only through a hand-written <c>JsonSerializerContext</c> in another assembly, or a bespoke collection
/// shape. Registration is idempotent and last-call-wins; it is expected to happen once at startup
/// (module initialization), before any deserialization, and is safe to call concurrently.
/// </para>
/// <para>
/// A property type that is not registered simply falls back to the reflection pipeline (when dynamic
/// code is available) or to leaf-only field names (under AOT) — never to an error.
/// </para>
/// </remarks>
public static class ScalarValuePathTracking
{
    private static readonly ConcurrentDictionary<Type, Func<string, JsonConverter>> s_propertyFactories = new();
    private static readonly ConcurrentDictionary<Type, Func<string, JsonConverter>> s_containerFactories = new();
    private static readonly ConcurrentDictionary<Type, JsonConverter> s_validationConverters = new();

    /// <summary>
    /// Registers compile-time-closed converters for direct and optional scalar request binding.
    /// </summary>
    /// <typeparam name="TValue">The scalar value object type.</typeparam>
    /// <typeparam name="TPrimitive">The underlying primitive type.</typeparam>
    /// <param name="converter">An optional result-based converter whose direct-value parsing and writing must be preserved.</param>
    public static void RegisterScalar<TValue, TPrimitive>(ResultJsonConverter<TValue>? converter = null)
        where TValue : class, IScalarValue<TValue, TPrimitive>
        where TPrimitive : IComparable
    {
        s_validationConverters[typeof(TValue)] = converter is null
            ? new ValidatingJsonConverter<TValue, TPrimitive>()
            : new CollectingResultJsonConverter<TValue>(converter, scalar: true);
        s_validationConverters[typeof(Maybe<TValue>)] = new MaybeScalarValueJsonConverter<TValue, TPrimitive>();
    }

    /// <summary>
    /// Registers a result-based JSON converter whose failures are collected during request binding.
    /// </summary>
    /// <typeparam name="T">The validated value type.</typeparam>
    /// <param name="converter">The compile-time-closed result-based converter.</param>
    /// <param name="scalar">Whether unspecified field labels identify the scalar itself rather than its children.</param>
    public static void RegisterResultConverter<T>(ResultJsonConverter<T> converter, bool scalar = false)
    {
        ArgumentNullException.ThrowIfNull(converter);
        s_validationConverters[typeof(T)] = new CollectingResultJsonConverter<T>(converter, scalar);
    }

    /// <summary>
    /// Registers a scalar property type so failures use its effective JSON property name under Native
    /// AOT, including names selected by a naming policy or <c>JsonPropertyNameAttribute</c>.
    /// </summary>
    /// <typeparam name="T">The scalar or optional scalar property type.</typeparam>
    public static void RegisterProperty<T>() =>
        s_propertyFactories[typeof(T)] = static name => new PathTrackingPropertyConverter<T>(name);

    /// <summary>
    /// Registers a nested-object property type so failures inside it report the full path
    /// (e.g. <c>/contact/email</c> rather than <c>/email</c>).
    /// </summary>
    /// <typeparam name="T">The object property type appearing in a DTO — for example <c>AddressDto</c>
    /// for a <c>Contact</c> property.</typeparam>
    public static void RegisterObject<T>() =>
        s_containerFactories[typeof(T)] = static name => new PathTrackingObjectConverter<T>(name);

    /// <summary>
    /// Registers a collection property type so failures inside its elements report an index-precise
    /// path (e.g. <c>/members/0/email</c>).
    /// </summary>
    /// <typeparam name="TCollection">The declared collection property type — <c>List&lt;TElement&gt;</c>,
    /// <c>TElement[]</c>, or an interface (<c>IList</c>/<c>ICollection</c>/<c>IEnumerable</c>/
    /// <c>IReadOnlyList</c>/<c>IReadOnlyCollection</c> of <typeparamref name="TElement"/>) that
    /// <c>List&lt;TElement&gt;</c> is assignable to.</typeparam>
    /// <typeparam name="TElement">The element type.</typeparam>
    public static void RegisterCollection<TCollection, TElement>() =>
        s_containerFactories[typeof(TCollection)] = static name => new PathTrackingCollectionConverter<TCollection, TElement>(name);

    /// <summary>
    /// Registers a string-keyed dictionary property type so failures inside its values report a
    /// key-precise path (e.g. <c>/prices/USD/amount</c>).
    /// </summary>
    /// <typeparam name="TDictionary">The declared dictionary property type —
    /// <c>Dictionary&lt;string, TValue&gt;</c>, or an interface
    /// (<c>IDictionary</c>/<c>IReadOnlyDictionary</c> of <c>string</c> and
    /// <typeparamref name="TValue"/>) that it is assignable to.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <remarks>
    /// Only string-keyed dictionaries are supported: a non-string key has no faithful RFC 6901
    /// rendering, so those shapes fall back to leaf-only paths rather than emitting a pointer a client
    /// cannot map back to its input.
    /// </remarks>
    public static void RegisterDictionary<TDictionary, TValue>() =>
        s_containerFactories[typeof(TDictionary)] = static name => new PathTrackingDictionaryConverter<TDictionary, TValue>(name);

    /// <summary>
    /// Creates the registered scalar-property converter for <paramref name="propertyType"/>, if any.
    /// </summary>
    internal static JsonConverter? TryCreateProperty(Type propertyType, string propertyName) =>
        s_propertyFactories.TryGetValue(propertyType, out var factory) ? factory(propertyName) : null;

    /// <summary>
    /// Creates the registered container converter for <paramref name="propertyType"/>, if any.
    /// </summary>
    internal static JsonConverter? TryCreateContainer(Type propertyType, string propertyName) =>
        s_containerFactories.TryGetValue(propertyType, out var factory) ? factory(propertyName) : null;

    internal static bool HasPropertyRegistrations => !s_propertyFactories.IsEmpty;

    internal static bool HasContainerRegistrations => !s_containerFactories.IsEmpty;

    internal static JsonConverter? TryGetValidationConverter(Type valueType) =>
        s_validationConverters.TryGetValue(valueType, out var converter) ? converter : null;

    /// <summary>
    /// Removes every registration. Test-only hook; the registry is process-wide startup state in
    /// production and is never cleared there.
    /// </summary>
    internal static void ClearForTests()
    {
        s_propertyFactories.Clear();
        s_containerFactories.Clear();
        s_validationConverters.Clear();
    }

}