namespace Trellis.Authorization;

/// <summary>
/// Declares that this resource has a single outbound navigation to a related resource
/// of type <typeparamref name="TRelated"/> identified by <typeparamref name="TId"/>.
/// Used by the pipeline to walk a chain of resources for indirect (multi-hop) resource
/// authorization.
/// </summary>
/// <typeparam name="TRelated">The related resource type the navigation points at.</typeparam>
/// <typeparam name="TId">The identifier type of the related resource.</typeparam>
/// <remarks>
/// <para>
/// Implement on aggregate roots whose ownership/authorization is evaluated against a
/// different aggregate one or more hops away. Each hop declared on the chain is loaded
/// via the existing <see cref="SharedResourceLoaderById{TResource, TId}"/> registration
/// for <typeparamref name="TRelated"/>.
/// </para>
/// <para>
/// For fan-out (multiple related resources of the same type, e.g. a cricket
/// <c>Match</c> with home and away teams), implement
/// <see cref="IIdentifyRelatedResources{TRelated, TId}"/> instead.
/// </para>
/// <para>
/// Paired with <see cref="IAuthorizeResourceVia{TOwner}"/> on the command. The pipeline
/// resolves a single non-cycling path from the command's leaf resource to its declared
/// owner type at registration time; runtime hops follow that pre-computed path.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class Match : Aggregate&lt;MatchId&gt;, IIdentifyRelatedResource&lt;Tournament, TournamentId&gt;
/// {
///     public TournamentId TournamentId { get; }
///     public TournamentId GetRelatedResourceId() =&gt; TournamentId;
/// }
/// </code>
/// </example>
public interface IIdentifyRelatedResource<TRelated, out TId>
{
    /// <summary>
    /// Returns the identifier of the related resource of type <typeparamref name="TRelated"/>.
    /// </summary>
    /// <returns>The typed identifier.</returns>
    TId GetRelatedResourceId();
}