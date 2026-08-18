namespace Trellis.Authorization;

/// <summary>
/// Declares that this resource has a plural outbound navigation to a set of related
/// resources of type <typeparamref name="TRelated"/> identified by <typeparamref name="TId"/>.
/// Used at the terminal hop of an indirect authorization chain to express OR-style /
/// candidate-set authorization (e.g. cricket <c>Match → {HomeTeam, AwayTeam}</c>).
/// </summary>
/// <typeparam name="TRelated">The related resource type.</typeparam>
/// <typeparam name="TId">The identifier type of the related resource.</typeparam>
/// <remarks>
/// <para>
/// The list semantics are:
/// <list type="bullet">
///   <item><description>Empty list is treated as "no candidates" and produces a
///   <see cref="Error.Forbidden"/> result without invoking the command's
///   <see cref="IAuthorizeResourceVia{TOwner}.Authorize"/>.</description></item>
///   <item><description>Duplicate IDs are de-duplicated by the pipeline before loading.</description></item>
///   <item><description>Order is not significant. If your domain needs to distinguish
///   roles (e.g., <c>HomeTeam</c> vs <c>AwayTeam</c>), use distinct types for each role
///   or drop to <c>IResourceLoader&lt;TMessage, TProjection&gt;</c>.</description></item>
/// </list>
/// </para>
/// <para>
/// Only supported at the terminal hop of an authorization chain in v1. Plural-in-middle
/// (fan-out cartesian expansion) is intentionally out of scope and produces a startup
/// error. Use <c>IResourceLoader&lt;TMessage, TProjection&gt;</c> for that shape.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class Match : Aggregate&lt;MatchId&gt;, IIdentifyRelatedResources&lt;Team, TeamId&gt;
/// {
///     public TeamId Team1Id { get; }
///     public TeamId Team2Id { get; }
///     public IReadOnlyList&lt;TeamId&gt; GetRelatedResourceIds() =&gt; [Team1Id, Team2Id];
/// }
/// </code>
/// </example>
public interface IIdentifyRelatedResources<TRelated, TId>
{
    /// <summary>
    /// Returns the identifiers of all related resources of type <typeparamref name="TRelated"/>.
    /// </summary>
    /// <returns>
    /// A non-null read-only list. Duplicates are de-duplicated by the pipeline before loading.
    /// An empty list short-circuits authorization to <see cref="Error.Forbidden"/>.
    /// </returns>
    IReadOnlyList<TId> GetRelatedResourceIds();
}