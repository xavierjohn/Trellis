namespace Trellis.Mediator;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Trellis.Authorization;

/// <summary>
/// Resolves a <see cref="ResolvedAuthorizationPath"/> from a leaf resource type to an
/// owner resource type by walking the entity graph defined by
/// <see cref="IIdentifyRelatedResource{TRelated, TId}"/> and
/// <see cref="IIdentifyRelatedResources{TRelated, TId}"/> declarations on candidate entity types.
/// </summary>
/// <remarks>
/// <para>
/// Resolution rules:
/// <list type="bullet">
///   <item><description><b>Distinct simple paths.</b> The resolver enumerates all simple paths
///   (no node visited twice on the same path) from <c>leafType</c> to <c>ownerType</c>. Cycles
///   in the entity graph are tolerated; only the cycle-free continuation matters.</description></item>
///   <item><description><b>Zero paths</b> → <see cref="InvalidOperationException"/> at resolve time.</description></item>
///   <item><description><b>More than one path</b> → <see cref="InvalidOperationException"/> at resolve
///   time, listing all discovered paths. Disambiguate by removing an
///   <c>IIdentifyRelatedResource[s]</c> declaration, by changing the command's <c>TOwner</c>, or
///   by dropping to <c>IResourceLoader&lt;TMessage, TProjection&gt;</c> for a custom shape.</description></item>
///   <item><description><b>Exactly one path</b> → the resolver builds typed extractor and loader
///   delegates and wraps them in a <see cref="ResolvedAuthorizationPath"/>. Plural-middle is
///   rejected by the path constructor; the resolver surfaces a clearer message.</description></item>
/// </list>
/// </para>
/// <para>
/// The resolver uses reflection to enumerate interface declarations and to construct typed
/// delegates. It is <b>not</b> AOT-safe; consumers targeting Native AOT should use an explicit
/// registration helper instead — see
/// <c>ServiceCollectionExtensions.AddRelatedResourceAuthorization&lt;TMessage, TLeaf, TLeafId, TOwner, TOwnerId, TResponse&gt;(extractOwnerId)</c>
/// for the single-hop case or
/// <c>ServiceCollectionExtensions.AddRelatedResourceAuthorization&lt;TMessage, TLeaf, TOwner, TResponse&gt;(path)</c>
/// when accepting a hand-built path.
/// </para>
/// </remarks>
public static class ResourceAuthorizationPathResolver
{
    /// <summary>
    /// Resolves the authorization path for a command type from leaf to owner.
    /// </summary>
    /// <param name="messageType">The command/query type that implements <see cref="IAuthorizeResourceVia{TOwner}"/>.</param>
    /// <param name="leafType">The leaf resource type the command identifies.</param>
    /// <param name="ownerType">The owner resource type authorization is evaluated against.</param>
    /// <param name="candidateEntityTypes">
    /// The set of concrete entity types to scan for <see cref="IIdentifyRelatedResource{TRelated, TId}"/>
    /// and <see cref="IIdentifyRelatedResources{TRelated, TId}"/> declarations. Typically the
    /// types in the assemblies passed to <see cref="ServiceCollectionExtensions"/> scanning.
    /// </param>
    /// <returns>The single resolved path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no path exists, multiple distinct simple paths exist, or a plural hop appears
    /// in a non-terminal position on the resolved path.
    /// </exception>
    [RequiresUnreferencedCode("Walks IIdentifyRelatedResource[s] interfaces by reflection.")]
    [RequiresDynamicCode("Constructs typed delegates via MakeGenericMethod.")]
    public static ResolvedAuthorizationPath Resolve(
        Type messageType,
        Type leafType,
        Type ownerType,
        IReadOnlyCollection<Type> candidateEntityTypes)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(leafType);
        ArgumentNullException.ThrowIfNull(ownerType);
        ArgumentNullException.ThrowIfNull(candidateEntityTypes);

        if (leafType == ownerType)
            throw new InvalidOperationException(
                $"Cannot resolve via-authorization path for {messageType.Name}: leafType and ownerType " +
                $"are both {leafType.Name}. Use IAuthorizeResource<{leafType.Name}> for zero-hop authorization " +
                $"against the leaf resource itself.");

        var graph = BuildAdjacencyGraph(candidateEntityTypes);

        var paths = new List<List<Edge>>();
        var stack = new List<Edge>();
        var visited = new HashSet<Type> { leafType };
        Dfs(leafType, ownerType, graph, visited, stack, paths);

        if (paths.Count == 0)
            throw new InvalidOperationException(
                $"Cannot resolve via-authorization path for {messageType.Name}: no path exists from " +
                $"{leafType.Name} to {ownerType.Name}. Declare IIdentifyRelatedResource<{ownerType.Name}, ...> " +
                $"on {leafType.Name} (single FK) or IIdentifyRelatedResources<{ownerType.Name}, ...> " +
                $"(fan-out, terminal-only) somewhere on the leaf-to-owner navigation chain. For shapes " +
                $"the resolver cannot infer (composite joins, projections, conditional paths, runtime " +
                $"queries), drop to the documented escape hatch: register a custom " +
                $"IResourceLoader<{messageType.Name}, TProjection> that returns the desired shape and " +
                $"put IAuthorizeResource<TProjection> on the command (zero-hop authorization against the " +
                $"projection), or call AddRelatedResourceAuthorization<{messageType.Name}, {leafType.Name}, " +
                $"{ownerType.Name}, TResponse>(this IServiceCollection, ResolvedAuthorizationPath) with a " +
                $"hand-built path.");

        if (paths.Count > 1)
            throw new InvalidOperationException(
                $"Cannot resolve via-authorization path for {messageType.Name}: {paths.Count} distinct simple " +
                $"paths from {leafType.Name} to {ownerType.Name} were found, but exactly one is required. " +
                $"Discovered paths:" + Environment.NewLine +
                string.Join(Environment.NewLine, paths.Select((p, i) => $"  [{i}] {FormatPath(leafType, p)}")) +
                Environment.NewLine + "Disambiguate by removing an IIdentifyRelatedResource[s] declaration, " +
                $"changing the command's TOwner, or dropping to a custom " +
                $"IResourceLoader<{messageType.Name}, TProjection> + IAuthorizeResource<TProjection> for a " +
                $"shape the resolver cannot disambiguate automatically.");

        var winning = paths[0];

        for (var i = 0; i < winning.Count - 1; i++)
        {
            if (winning[i].IsPlural)
                throw new InvalidOperationException(
                    $"Cannot resolve via-authorization path for {messageType.Name}: hop {i} " +
                    $"({winning[i].FromType.Name} -> {winning[i].ToType.Name}) is plural but is not the " +
                    $"terminal hop. Plural-in-middle (fan-out cartesian expansion) is intentionally out of " +
                    $"scope for v1; flatten the chain into a custom IResourceLoader<{messageType.Name}, " +
                    $"TProjection> and authorize via IAuthorizeResource<TProjection>.");
        }

        var hops = new ResolvedAuthorizationHop[winning.Count];
        for (var i = 0; i < winning.Count; i++)
            hops[i] = BuildHop(winning[i]);

        return new ResolvedAuthorizationPath(messageType, leafType, ownerType, hops);
    }

    [RequiresUnreferencedCode("Walks IIdentifyRelatedResource[s] interfaces by reflection over candidate entity types.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075:'this' argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method.",
        Justification = "Resolver is documented as not AOT-safe; consumers targeting Native AOT use the explicit registration helper.")]
    private static Dictionary<Type, List<Edge>> BuildAdjacencyGraph(IReadOnlyCollection<Type> types)
    {
        var graph = new Dictionary<Type, List<Edge>>();
        var singularDef = typeof(IIdentifyRelatedResource<,>);
        var pluralDef = typeof(IIdentifyRelatedResources<,>);

        // Deduplicate candidate types up front. Caller may legitimately pass overlapping
        // assemblies or assembly-scan results that include the same type more than once.
        // Without this guard the same outbound edge would be added twice on the same source
        // type, and DFS would count duplicate edges as distinct simple paths and falsely
        // throw "multiple distinct simple paths" at the ambiguity check.
        var seenTypes = new HashSet<Type>();

        foreach (var t in types)
        {
            if (t is null || t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition)
                continue;
            if (!seenTypes.Add(t))
                continue;

            foreach (var iface in t.GetInterfaces())
            {
                if (!iface.IsGenericType)
                    continue;

                var def = iface.GetGenericTypeDefinition();
                bool isPlural;
                if (def == singularDef)
                    isPlural = false;
                else if (def == pluralDef)
                    isPlural = true;
                else
                    continue;

                var args = iface.GetGenericArguments();
                var toType = args[0];
                var toIdType = args[1];

                // Find the GetRelatedResourceId(s) method on the interface itself (not the impl)
                // so the delegate can dispatch virtually through the interface.
                var methodName = isPlural ? nameof(IIdentifyRelatedResources<object, object>.GetRelatedResourceIds)
                    : nameof(IIdentifyRelatedResource<object, object>.GetRelatedResourceId);
                var getMethod = iface.GetMethod(methodName)
                    ?? throw new InvalidOperationException(
                        $"Could not find {methodName} on {iface.Name}. This indicates a framework bug.");

                if (!graph.TryGetValue(t, out var edges))
                {
                    edges = [];
                    graph[t] = edges;
                }

                edges.Add(new Edge(t, toType, toIdType, isPlural, getMethod));
            }
        }

        return graph;
    }

    private static void Dfs(
        Type current,
        Type target,
        Dictionary<Type, List<Edge>> graph,
        HashSet<Type> visited,
        List<Edge> stack,
        List<List<Edge>> paths)
    {
        if (!graph.TryGetValue(current, out var edges))
            return;

        foreach (var edge in edges)
        {
            if (edge.ToType == target)
            {
                stack.Add(edge);
                paths.Add([.. stack]);
                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            if (visited.Contains(edge.ToType))
                continue;

            visited.Add(edge.ToType);
            stack.Add(edge);
            Dfs(edge.ToType, target, graph, visited, stack, paths);
            stack.RemoveAt(stack.Count - 1);
            visited.Remove(edge.ToType);
        }
    }

    [RequiresDynamicCode("Constructs typed delegates via MakeGenericMethod.")]
    private static ResolvedAuthorizationHop BuildHop(Edge edge)
    {
        // Nullable<T> is unsupported as TId because the SingularExtractorImpl /
        // PluralExtractorImpl helpers carry `where TId : notnull` — a Nullable<T>
        // would otherwise fail with a confusing reflection error at MakeGenericMethod
        // time. Reject only for hops on the *resolved* path so that unrelated entities
        // declaring optional navigations do not crash the scanner for via-commands that
        // never traverse them.
        if (edge.ToIdType.IsGenericType && edge.ToIdType.GetGenericTypeDefinition() == typeof(Nullable<>))
            throw new InvalidOperationException(
                $"{edge.FromType.Name} declares a related-resource hop to {edge.ToType.Name} " +
                $"with a Nullable<{Nullable.GetUnderlyingType(edge.ToIdType)!.Name}> identifier, " +
                $"which is not supported on a resolved authorization path. Change the interface " +
                $"declaration to the non-nullable underlying type ({Nullable.GetUnderlyingType(edge.ToIdType)!.Name}). " +
                $"To model an optional relationship: for *plural* navigations, return an empty list " +
                $"from GetRelatedResourceIds when no related resource is present — this short-circuits " +
                $"authorization to Forbidden. For *optional singular* navigations the resolver cannot " +
                $"automatically express the \"no related resource\" case; drop to the documented escape " +
                $"hatch — a custom IResourceLoader<TMessage, TProjection> + IAuthorizeResource<TProjection>, " +
                $"or AddRelatedResourceAuthorization(..., ResolvedAuthorizationPath) with a hand-built " +
                $"path whose loader handles the absent-related-resource case explicitly.");

        var extractIds = edge.IsPlural
            ? BuildPluralExtractor(edge.FromType, edge.ToIdType, edge.GetMethod)
            : BuildSingularExtractor(edge.FromType, edge.ToIdType, edge.GetMethod);

        var loadAsync = BuildLoader(edge.ToType, edge.ToIdType);

        return new ResolvedAuthorizationHop(
            edge.FromType, edge.ToType, edge.ToIdType, extractIds, loadAsync, edge.IsPlural);
    }

    [RequiresDynamicCode("Constructs typed delegates via MakeGenericMethod.")]
    private static Func<object, IReadOnlyList<object>> BuildSingularExtractor(
        Type fromType, Type idType, MethodInfo getMethod)
    {
        var helper = typeof(ResourceAuthorizationPathResolver)
            .GetMethod(nameof(SingularExtractorImpl), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(fromType, idType);
        return (Func<object, IReadOnlyList<object>>)helper.Invoke(null, [getMethod])!;
    }

    [RequiresDynamicCode("Constructs typed delegates via MakeGenericMethod.")]
    private static Func<object, IReadOnlyList<object>> BuildPluralExtractor(
        Type fromType, Type idType, MethodInfo getMethod)
    {
        var helper = typeof(ResourceAuthorizationPathResolver)
            .GetMethod(nameof(PluralExtractorImpl), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(fromType, idType);
        return (Func<object, IReadOnlyList<object>>)helper.Invoke(null, [getMethod])!;
    }

    [RequiresDynamicCode("Constructs typed delegates via MakeGenericMethod.")]
    private static Func<IServiceProvider, object, CancellationToken, Task<HopLoadResult>> BuildLoader(
        Type toType, Type idType)
    {
        var helper = typeof(ResourceAuthorizationPathResolver)
            .GetMethod(nameof(LoaderImpl), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(toType, idType);
        return (Func<IServiceProvider, object, CancellationToken, Task<HopLoadResult>>)helper.Invoke(null, null)!;
    }

    private static Func<object, IReadOnlyList<object>> SingularExtractorImpl<TFrom, TId>(MethodInfo getMethod)
        where TId : notnull
    {
        // Interface method dispatched virtually via Delegate.CreateDelegate against the interface MethodInfo.
        var d = getMethod.CreateDelegate<Func<TFrom, TId>>();
        return src =>
        {
            var id = d((TFrom)src);
            return id is null ? Array.Empty<object>() : new object[] { id };
        };
    }

    private static Func<object, IReadOnlyList<object>> PluralExtractorImpl<TFrom, TId>(MethodInfo getMethod)
        where TId : notnull
    {
        var d = getMethod.CreateDelegate<Func<TFrom, IReadOnlyList<TId>>>();
        return src =>
        {
            var ids = d((TFrom)src);
            if (ids is null || ids.Count == 0)
                return Array.Empty<object>();
            var arr = new object[ids.Count];
            for (var i = 0; i < ids.Count; i++)
                arr[i] = ids[i]!;
            return arr;
        };
    }

    private static Func<IServiceProvider, object, CancellationToken, Task<HopLoadResult>> LoaderImpl<TTo, TId>()
        where TTo : class
        => async (sp, id, ct) =>
        {
            // Missing loader is a DEPLOYMENT bug — throw to fail loud rather than masking
            // it as a 403 Forbidden. Persistent 403s caused by missing loader registrations
            // are very hard to distinguish from real authorization denials in production.
            var loader = (SharedResourceLoaderById<TTo, TId>?)sp.GetService(typeof(SharedResourceLoaderById<TTo, TId>))
                ?? throw new InvalidOperationException(
                    $"Indirect resource authorization requires a registered " +
                    $"SharedResourceLoaderById<{typeof(TTo).Name}, {typeof(TId).Name}> for the navigation hop. " +
                    $"Register one in the composition root or scan the assembly that contains it via " +
                    $"AddResourceAuthorization(...).");

            var result = await loader.GetByIdAsync((TId)id, ct).ConfigureAwait(false);
            if (!result.TryGetValue(out var v, out var err))
                return HopLoadResult.Failure(err);

            // Defense-in-depth: a SharedResourceLoaderById that violates its Result<T>
            // contract by returning a successful result carrying a null payload must NOT
            // crash the pipeline with ArgumentNullException from HopLoadResult.Success —
            // that would bubble as an unhandled exception and violate the documented
            // "intermediate / owner load failure collapses to Forbidden" invariant on
            // ResourceAuthorizationViaBehavior. Fail closed instead.
            if (v is null)
                return HopLoadResult.Failure(new Error.Forbidden("resource.authorization-via.null-payload")
                {
                    Detail = "A related resource loader returned a successful result with a null value.",
                });

            return HopLoadResult.Success(v);
        };

    private static string FormatPath(Type leafType, List<Edge> edges)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(leafType.Name);
        foreach (var e in edges)
        {
            sb.Append(e.IsPlural ? " =[*]=> " : " --> ");
            sb.Append(e.ToType.Name);
        }

        return sb.ToString();
    }

    private sealed record Edge(Type FromType, Type ToType, Type ToIdType, bool IsPlural, MethodInfo GetMethod);
}