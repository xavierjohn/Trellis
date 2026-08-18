namespace Trellis.Mediator;

using Trellis.Authorization;

/// <summary>
/// Result of loading a single related resource at one ID during a hop walk.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="HopLoadResult"/> uses an explicit success flag rather than inferring
/// success from the presence of an error. The <c>default</c> instance is a failure
/// (with a sentinel <see cref="Error"/>) so a misconfigured hop loader cannot
/// accidentally produce a "successful" result carrying <c>null</c> as the loaded
/// value, which would bypass the empty-list / failed-load short-circuits in
/// <see cref="ResourceAuthorizationViaBehavior{TMessage, TLeaf, TOwner, TResponse}"/>.
/// </para>
/// </remarks>
public readonly struct HopLoadResult
{
    private static readonly Error s_defaultUninitialized = new Error.Forbidden("resource.authorization-via.hop-uninitialized")
    {
        Detail = "default(HopLoadResult) is not a valid load result. Use HopLoadResult.Success(value) or HopLoadResult.Failure(error).",
    };

    private readonly bool _isSuccess;
    private readonly object? _value;
    private readonly Error? _error;

    private HopLoadResult(bool isSuccess, object? value, Error? error)
    {
        _isSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    /// <summary>
    /// Creates a success result carrying the loaded value.
    /// </summary>
    /// <param name="value">The loaded resource. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is null.</exception>
    public static HopLoadResult Success(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new HopLoadResult(isSuccess: true, value, error: null);
    }

    /// <summary>
    /// Creates a failure result carrying the loader's error.
    /// </summary>
    /// <param name="error">The loader's error. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is null.</exception>
    public static HopLoadResult Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new HopLoadResult(isSuccess: false, value: null, error);
    }

    /// <summary>Gets the loaded value when successful; <c>null</c> when failed.</summary>
    public object? Value => _value;

    /// <summary>Gets the loader's error when failed or uninitialized; <c>null</c> when successful.</summary>
    public Error? Error => _isSuccess ? null : (_error ?? s_defaultUninitialized);

    /// <summary>
    /// Gets a value indicating whether the load succeeded. <see langword="false"/> for
    /// <c>default(HopLoadResult)</c> so a misconfigured hop loader cannot accidentally
    /// pass a null value through the pipeline.
    /// </summary>
    public bool IsSuccess => _isSuccess;
}

/// <summary>
/// Pre-computed navigation path from a leaf resource to an owner resource for
/// indirect (multi-hop) resource authorization. Constructed at registration time
/// (either by the assembly-scanning resolver or by an explicit registration helper)
/// and passed to <see cref="ResourceAuthorizationViaBehavior{TMessage, TLeaf, TOwner, TResponse}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per-message registration.</b> Each command has its own <see cref="ResolvedAuthorizationPath"/>.
/// Do <b>not</b> register <see cref="ResolvedAuthorizationPath"/> as an unkeyed DI service —
/// that would silently share a single path across all via-authorized commands, allowing one
/// command's authorization rules to apply to another. Use one of the supported registration
/// paths instead:
/// <list type="bullet">
///   <item><description><b>Assembly scanning</b>:
///   <c>services.AddResourceAuthorization(typeof(MyCommand).Assembly)</c> discovers
///   via-commands, runs the resolver, and registers each path as a typed
///   <see cref="ResolvedAuthorizationPathHolder{TMessage, TLeaf, TOwner, TResponse}"/>.</description></item>
///   <item><description><b>Explicit (AOT-friendly)</b>:
///   <c>services.AddRelatedResourceAuthorization&lt;TMessage, TLeaf, TLeafId, TOwner, TOwnerId, TResponse&gt;(extractOwnerId)</c>
///   for single-hop, or <c>services.AddRelatedResourceAuthorization&lt;TMessage, TLeaf, TOwner, TResponse&gt;(path)</c>
///   accepting a hand-built path. Both overloads register the path as a closed-generic
///   <see cref="ResolvedAuthorizationPathHolder{TMessage, TLeaf, TOwner, TResponse}"/> and the
///   behavior as a typed descriptor — preserving relocation before
///   <c>ValidationBehavior</c>.</description></item>
/// </list>
/// As a defense-in-depth measure, the behavior's constructor validates that the path's
/// <see cref="MessageType"/>, <see cref="LeafType"/>, and <see cref="OwnerType"/> agree with
/// its own generic arguments and fails fast when they do not.
/// </para>
/// <para>
/// Each <see cref="ResolvedAuthorizationHop"/> in <see cref="Hops"/> describes a single
/// load step: starting from one or more instances of <see cref="ResolvedAuthorizationHop.FromType"/>,
/// the pipeline extracts the IDs of related instances of type
/// <see cref="ResolvedAuthorizationHop.ToType"/>, de-duplicates them, and loads each via
/// the delegate carried on the hop.
/// </para>
/// <para>
/// Topology invariants enforced at construction time:
/// <list type="bullet">
///   <item><description><see cref="Hops"/> must be non-empty.</description></item>
///   <item><description>For all adjacent hops, <c>hops[i].ToType</c> must equal <c>hops[i+1].FromType</c> (chain continuity).</description></item>
///   <item><description>The first hop's <see cref="ResolvedAuthorizationHop.FromType"/> must equal <see cref="LeafType"/>.</description></item>
///   <item><description>The terminal hop's <see cref="ResolvedAuthorizationHop.ToType"/> must equal <see cref="OwnerType"/>.</description></item>
///   <item><description>At most one hop is plural, and if present it must be the terminal hop. Plural-in-middle (fan-out cartesian expansion) is intentionally out of scope for v1; use <c>IResourceLoader&lt;TMessage, TProjection&gt;</c> with a projection type instead.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class ResolvedAuthorizationPath
{
    /// <summary>
    /// Creates a new resolved authorization path.
    /// </summary>
    /// <param name="messageType">The command/query type this path serves.</param>
    /// <param name="leafType">The leaf resource type the command identifies.</param>
    /// <param name="ownerType">The owner resource type authorization is evaluated against.</param>
    /// <param name="hops">
    /// The ordered list of hops from leaf to owner. The list is copied defensively, so
    /// subsequent mutation of the caller's collection cannot alter the path after construction.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="hops"/> is empty, when adjacent hops do not chain
    /// (<c>hops[i].ToType != hops[i+1].FromType</c>), when the first hop's
    /// <see cref="ResolvedAuthorizationHop.FromType"/> does not equal <paramref name="leafType"/>,
    /// when the terminal hop's <see cref="ResolvedAuthorizationHop.ToType"/> does not equal
    /// <paramref name="ownerType"/>, or when a plural hop appears at a non-terminal position.
    /// </exception>
    public ResolvedAuthorizationPath(
        Type messageType,
        Type leafType,
        Type ownerType,
        IReadOnlyList<ResolvedAuthorizationHop> hops)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(leafType);
        ArgumentNullException.ThrowIfNull(ownerType);
        ArgumentNullException.ThrowIfNull(hops);

        if (hops.Count == 0)
            throw new ArgumentException("Resolved authorization path must contain at least one hop.", nameof(hops));

        // Defensive copy so post-construction mutation of the caller's list can't alter the path.
        var copy = new ResolvedAuthorizationHop[hops.Count];
        for (var i = 0; i < hops.Count; i++)
        {
            var h = hops[i] ?? throw new ArgumentException($"hops[{i}] is null.", nameof(hops));
            copy[i] = h;
        }

        if (copy[0].FromType != leafType)
            throw new ArgumentException(
                $"First hop's FromType ({copy[0].FromType.Name}) must equal leafType ({leafType.Name}). " +
                "The path's first hop must originate at the leaf resource the command identifies.",
                nameof(hops));

        var terminal = copy[copy.Length - 1];
        if (terminal.ToType != ownerType)
            throw new ArgumentException(
                $"Terminal hop's ToType ({terminal.ToType.Name}) must equal ownerType ({ownerType.Name}). " +
                "The path must end at the owner resource the command authorizes against.",
                nameof(hops));

        for (var i = 0; i < copy.Length - 1; i++)
        {
            if (copy[i].IsPlural)
                throw new ArgumentException(
                    $"Plural hop is only supported at the terminal position (hops[{copy.Length - 1}]). " +
                    $"Hop at index {i} ({copy[i].FromType.Name} -> {copy[i].ToType.Name}) is plural but not terminal. " +
                    $"Plural-in-middle (fan-out cartesian expansion) is intentionally out of scope for v1; " +
                    $"use IResourceLoader<TMessage, TProjection> with a projection type instead.",
                    nameof(hops));

            if (copy[i].ToType != copy[i + 1].FromType)
                throw new ArgumentException(
                    $"Hops do not chain: hops[{i}].ToType ({copy[i].ToType.Name}) != hops[{i + 1}].FromType ({copy[i + 1].FromType.Name}). " +
                    "Each hop's destination type must equal the next hop's source type so the walk is well-formed.",
                    nameof(hops));
        }

        MessageType = messageType;
        LeafType = leafType;
        OwnerType = ownerType;
        Hops = copy;
    }

    /// <summary>Gets the command/query type this path serves.</summary>
    public Type MessageType { get; }

    /// <summary>Gets the leaf resource type the command identifies.</summary>
    public Type LeafType { get; }

    /// <summary>Gets the owner resource type authorization is evaluated against.</summary>
    public Type OwnerType { get; }

    /// <summary>Gets the ordered list of hops from leaf to owner.</summary>
    public IReadOnlyList<ResolvedAuthorizationHop> Hops { get; }
}

/// <summary>
/// Describes a single hop in an indirect authorization chain.
/// </summary>
public sealed class ResolvedAuthorizationHop
{
    /// <summary>
    /// Creates a new resolved hop.
    /// </summary>
    /// <param name="fromType">The CLR type of the resources this hop starts from.</param>
    /// <param name="toType">The CLR type of the resources this hop loads.</param>
    /// <param name="toIdType">The CLR type of the identifier used to load <paramref name="toType"/>.</param>
    /// <param name="extractIds">
    /// Delegate that, given an instance of <paramref name="fromType"/>, returns the related
    /// resource IDs of type <paramref name="toIdType"/>. For singular hops, returns a list of
    /// size 0 or 1; for plural hops, returns 0..N IDs.
    /// </param>
    /// <param name="loadAsync">
    /// Delegate that loads a single related resource by ID from the request-scoped service
    /// provider. Built at registration time so the runtime behavior remains AOT-clean.
    /// </param>
    /// <param name="isPlural">
    /// <c>true</c> when this hop is plural (the source declares
    /// <see cref="IIdentifyRelatedResources{TRelated, TId}"/>); <c>false</c> when singular.
    /// </param>
    public ResolvedAuthorizationHop(
        Type fromType,
        Type toType,
        Type toIdType,
        Func<object, IReadOnlyList<object>> extractIds,
        Func<IServiceProvider, object, CancellationToken, Task<HopLoadResult>> loadAsync,
        bool isPlural)
    {
        ArgumentNullException.ThrowIfNull(fromType);
        ArgumentNullException.ThrowIfNull(toType);
        ArgumentNullException.ThrowIfNull(toIdType);
        ArgumentNullException.ThrowIfNull(extractIds);
        ArgumentNullException.ThrowIfNull(loadAsync);

        FromType = fromType;
        ToType = toType;
        ToIdType = toIdType;
        ExtractIds = extractIds;
        LoadAsync = loadAsync;
        IsPlural = isPlural;
    }

    /// <summary>Gets the CLR type of the resources this hop starts from.</summary>
    public Type FromType { get; }

    /// <summary>Gets the CLR type of the resources this hop loads.</summary>
    public Type ToType { get; }

    /// <summary>Gets the CLR type of the identifier used to load <see cref="ToType"/>.</summary>
    public Type ToIdType { get; }

    /// <summary>
    /// Gets the delegate that extracts related-resource IDs from a single source instance.
    /// </summary>
    public Func<object, IReadOnlyList<object>> ExtractIds { get; }

    /// <summary>
    /// Gets the delegate that loads one resource by ID from the request-scoped service provider.
    /// </summary>
    public Func<IServiceProvider, object, CancellationToken, Task<HopLoadResult>> LoadAsync { get; }

    /// <summary>
    /// Gets a value indicating whether this hop is plural (fan-out). Only the terminal
    /// hop may be plural.
    /// </summary>
    public bool IsPlural { get; }
}