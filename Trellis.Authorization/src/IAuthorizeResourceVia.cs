namespace Trellis.Authorization;

/// <summary>
/// Declares resource-based authorization against a resource that is NOT the leaf
/// resource the command identifies, but is reachable via one or more hops along
/// <see cref="IIdentifyRelatedResource{TRelated, TId}"/> / <see cref="IIdentifyRelatedResources{TRelated, TId}"/>
/// declarations on the entities along the path.
/// </summary>
/// <typeparam name="TOwner">
/// The resource type at the end of the navigation chain that authorization is evaluated
/// against. The pipeline loads one or more instances of this type and passes them to
/// <see cref="Authorize"/>.
/// </typeparam>
/// <remarks>
/// <para>
/// The command identifies its leaf resource via the existing
/// <see cref="IIdentifyResource{TResource, TId}"/> interface (or supplies a custom
/// <see cref="IResourceLoader{TMessage, TResource}"/>); this interface declares only the
/// final authorization target. The path from leaf to <typeparamref name="TOwner"/> is
/// resolved at registration time from the entity-side
/// <see cref="IIdentifyRelatedResource{TRelated, TId}"/> /
/// <see cref="IIdentifyRelatedResources{TRelated, TId}"/> declarations.
/// </para>
/// <para>
/// <see cref="Authorize"/> always receives <see cref="IReadOnlyList{TOwner}"/>:
/// size 1 for chains whose terminal hop is singular, size N for chains whose terminal
/// hop is plural (fan-out, e.g. cricket "actor owns home OR away team"). The framework
/// does not impose the operator over the list; the command's <see cref="Authorize"/>
/// method picks <c>Any</c>, <c>All</c>, or any other shape.
/// </para>
/// <para>
/// Implementing both <see cref="IAuthorizeResource{TResource}"/> and
/// <see cref="IAuthorizeResourceVia{TOwner}"/> on the same command is rejected at
/// startup. Security primitives are not silently composed.
/// </para>
/// <para>
/// Cases not expressible by the navigation-chain model — composite keys, joins,
/// projections, conditional / data-dependent paths, recursive hierarchies, or
/// authorization needing multiple heterogeneous resources at once — should fall back
/// to a custom <see cref="IResourceLoader{TMessage, TResource}"/> that returns a
/// projection type, plus <see cref="IAuthorizeResource{TResource}"/> on the command
/// over that projection.
/// </para>
/// </remarks>
/// <example>
/// Cricket fan-out (actor owns home OR away team):
/// <code>
/// public sealed record UploadScorecardCommand(MatchId MatchId)
///     : ICommand&lt;Result&lt;Unit&gt;&gt;,
///       IIdentifyResource&lt;Match, MatchId&gt;,
///       IAuthorizeResourceVia&lt;Team&gt;
/// {
///     public MatchId GetResourceId() =&gt; MatchId;
///
///     public IResult Authorize(Actor actor, IReadOnlyList&lt;Team&gt; teams) =&gt;
///         Result.Ensure(
///             teams.Any(t =&gt; t.CreatedByActorId == actor.Id),
///             new Error.Forbidden("not_team_owner"));
/// }
/// </code>
/// </example>
public interface IAuthorizeResourceVia<TOwner>
{
    /// <summary>
    /// Authorizes the actor against the resolved owner resources.
    /// </summary>
    /// <param name="actor">The authenticated actor performing the operation.</param>
    /// <param name="owners">
    /// The non-null, non-empty list of resources at the end of the navigation chain.
    /// For singular-terminal chains this is always size 1; for plural-terminal chains
    /// (fan-out) it is the de-duplicated set of loaded owners. The pipeline guarantees
    /// the list is non-empty; an empty plural navigation short-circuits to
    /// <see cref="Error.Forbidden"/> before <see cref="Authorize"/> is called.
    /// </param>
    /// <returns>
    /// A success result to proceed, or a failure result (typically
    /// <see cref="Error.Forbidden"/>) to short-circuit the pipeline.
    /// </returns>
    IResult Authorize(Actor actor, IReadOnlyList<TOwner> owners);
}