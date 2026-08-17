namespace Trellis.Mediator;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

/// <summary>
/// A bidirectional map between an integration event's stable wire name (see
/// <see cref="IntegrationEventNameAttribute"/>) and its local CLR type — the contract a broker transport
/// serializes through so producer and consumer never exchange assembly-qualified type names.
/// </summary>
/// <remarks>
/// <para>
/// The map is immutable and validated at construction: an unusable contract surfaces when the application
/// starts rather than when a message arrives at three in the morning. Every rejection is a contract bug
/// that cannot be recovered from at runtime — two events claiming one name would make the wire ambiguous,
/// and a name resolving to a type that is not an <see cref="IIntegrationEvent"/> could never be dispatched.
/// </para>
/// <para>
/// Lookups return <see cref="Maybe{T}"/> rather than throwing, because an <i>unknown</i> name is a normal
/// operational condition: a producer may legitimately emit events this consumer does not subscribe to, and
/// the transport should dead-letter or ignore them by policy rather than crash.
/// </para>
/// </remarks>
public sealed class IntegrationEventNameMap
{
    private readonly Dictionary<string, Type> _byName;
    private readonly Dictionary<Type, string> _byType;

    /// <summary>
    /// Creates a map from explicit name/type pairs. This overload is trimming- and NativeAOT-safe; prefer it
    /// over <see cref="FromAssemblies"/> when publishing a trimmed application.
    /// </summary>
    /// <param name="contracts">The wire name to CLR type pairs to register.</param>
    /// <exception cref="ArgumentNullException"><paramref name="contracts"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// A name is empty, a type does not implement <see cref="IIntegrationEvent"/>, is not concrete, or has
    /// unbound generic parameters, or a name or type appears more than once.
    /// </exception>
    public IntegrationEventNameMap(IEnumerable<KeyValuePair<string, Type>> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        _byName = new Dictionary<string, Type>(StringComparer.Ordinal);
        _byType = [];

        foreach (var (name, type) in contracts)
        {
            ArgumentNullException.ThrowIfNull(type, nameof(contracts));
            ArgumentException.ThrowIfNullOrWhiteSpace(name, nameof(contracts));

            if (!typeof(IIntegrationEvent).IsAssignableFrom(type))
                throw new ArgumentException(
                    $"Type '{type}' is mapped to integration event name '{name}' but does not implement {nameof(IIntegrationEvent)}.",
                    nameof(contracts));

            if (type is { IsAbstract: true } or { IsInterface: true })
                throw new ArgumentException(
                    $"Type '{type}' is mapped to integration event name '{name}' but is not a concrete type, so an " +
                    "incoming message could never be materialized as it.",
                    nameof(contracts));

            // An open generic passes the concrete-type check but is just as unusable: it would be keyed under
            // its type definition, so NameFor(GenericEvent<int>) misses, and TypeFor would hand a transport a
            // type it cannot instantiate. A wire contract has to name one closed type.
            if (type.ContainsGenericParameters)
                throw new ArgumentException(
                    $"Type '{type}' is mapped to integration event name '{name}' but has unbound generic " +
                    "parameters. Name a closed constructed type so the wire contract identifies exactly one shape.",
                    nameof(contracts));

            if (_byName.TryGetValue(name, out var existingType))
                throw new ArgumentException(
                    $"Integration event name '{name}' is declared by both '{existingType}' and '{type}'. A wire name " +
                    "must identify exactly one type or an incoming message would be ambiguous.",
                    nameof(contracts));

            if (_byType.TryGetValue(type, out var existingName))
                throw new ArgumentException(
                    $"Type '{type}' is mapped to both integration event names '{existingName}' and '{name}'. A type " +
                    "must publish under exactly one name or consumers could not agree on its contract.",
                    nameof(contracts));

            _byName.Add(name, type);
            _byType.Add(type, name);
        }
    }

    /// <summary>An empty map: every lookup returns <see cref="Maybe{T}.None"/>.</summary>
    public static IntegrationEventNameMap Empty { get; } = new([]);

    /// <summary>The wire names registered in this map.</summary>
    public IReadOnlyCollection<string> Names => _byName.Keys;

    /// <summary>
    /// Builds a map by scanning <paramref name="assemblies"/> for concrete <see cref="IIntegrationEvent"/>
    /// types carrying <see cref="IntegrationEventNameAttribute"/>. Types without the attribute are skipped,
    /// so a contract assembly can hold events that are deliberately in-process only.
    /// </summary>
    /// <param name="assemblies">The assemblies holding the integration event contracts.</param>
    /// <returns>The validated map.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assemblies"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Two scanned types declare the same wire name.</exception>
    [RequiresUnreferencedCode(
        "Scans assemblies for IIntegrationEvent types annotated with IntegrationEventNameAttribute; the types " +
        "may be trimmed. Use the IEnumerable<KeyValuePair<string, Type>> constructor in trimmed applications.")]
    public static IntegrationEventNameMap FromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var contracts =
            from assembly in assemblies
            from type in assembly.GetTypes()
            where type is { IsAbstract: false, IsInterface: false } && typeof(IIntegrationEvent).IsAssignableFrom(type)
            let attribute = type.GetCustomAttribute<IntegrationEventNameAttribute>(inherit: false)
            where attribute is not null
            select new KeyValuePair<string, Type>(attribute.Name, type);

        return new IntegrationEventNameMap(contracts);
    }

    /// <summary>Returns the wire name registered for <paramref name="type"/>, if any.</summary>
    /// <param name="type">The local CLR event type.</param>
    /// <returns>The wire name, or <see cref="Maybe{T}.None"/> when the type has no registered contract.</returns>
    public Maybe<string> NameFor(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _byType.TryGetValue(type, out var name) ? Maybe<string>.From(name) : Maybe<string>.None;
    }

    /// <summary>Returns the CLR type registered for <paramref name="name"/>, if any.</summary>
    /// <param name="name">The wire name read from the message.</param>
    /// <returns>The CLR type, or <see cref="Maybe{T}.None"/> when this application knows no such contract.</returns>
    public Maybe<Type> TypeFor(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.TryGetValue(name, out var type) ? Maybe<Type>.From(type) : Maybe<Type>.None;
    }

}
