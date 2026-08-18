namespace Trellis.Asp.Validation;

using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Registry of compile-time-closed path-tracking converters, keyed by the container property type
/// they wrap. It is the Native AOT counterpart to the reflection pipeline's runtime
/// <c>Type.MakeGenericType</c> construction: AOT forbids building a closed generic at runtime, so the
/// closed instantiations are created by the source generator at compile time and registered here.
/// </summary>
/// <remarks>
/// <para>
/// Consumers do not normally call this type. The Trellis ASP source generator emits a
/// <c>[ModuleInitializer]</c> that registers every container property type in the DTO graph reachable
/// from a <c>[JsonSerializable]</c>-annotated <c>JsonSerializerContext</c>, so index-precise validation
/// field paths (<c>/members/0/email</c>) work identically in reflection and Native AOT.
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
    private static readonly ConcurrentDictionary<Type, Func<string, JsonConverter>> s_factories = new();

    /// <summary>
    /// Registers a nested-object property type so failures inside it report the full path
    /// (e.g. <c>/contact/email</c> rather than <c>/email</c>).
    /// </summary>
    /// <typeparam name="T">The object property type appearing in a DTO — for example <c>AddressDto</c>
    /// for a <c>Contact</c> property.</typeparam>
    public static void RegisterObject<T>() =>
        s_factories[typeof(T)] = static name => new PathTrackingObjectConverter<T>(name);

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
        s_factories[typeof(TCollection)] = static name => new PathTrackingCollectionConverter<TCollection, TElement>(name);

    /// <summary>
    /// Creates the registered path-tracking converter for <paramref name="propertyType"/>, if any.
    /// </summary>
    internal static JsonConverter? TryCreate(Type propertyType, string propertyName) =>
        s_factories.TryGetValue(propertyType, out var factory) ? factory(propertyName) : null;

    /// <summary>
    /// True when at least one registration exists. Lets the modifier skip the registry lookup entirely
    /// for the overwhelmingly common reflection-mode case where the generator emitted nothing.
    /// </summary>
    internal static bool HasRegistrations => !s_factories.IsEmpty;

    /// <summary>
    /// Removes every registration. Test-only hook; the registry is process-wide startup state in
    /// production and is never cleared there.
    /// </summary>
    internal static void ClearForTests() => s_factories.Clear();
}