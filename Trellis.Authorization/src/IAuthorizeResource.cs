namespace Trellis.Authorization;

/// <summary>
/// Declares resource-based authorization that requires a loaded resource.
/// Implemented by the command/query. The pipeline loads the resource via
/// <see cref="IResourceLoader{TMessage, TResource}"/> and passes it to this method.
/// </summary>
/// <typeparam name="TResource">
/// The type of the resource that must be loaded before authorization can be evaluated.
/// </typeparam>
/// <remarks>
/// <para>
/// This interface receives the loaded resource — enabling ownership checks,
/// tenant isolation, and other data-dependent authorization rules.
/// </para>
/// <para>
/// The resource is loaded by an <see cref="IResourceLoader{TMessage, TResource}"/> registered in DI.
/// If loading fails (e.g., resource not found), the pipeline short-circuits with the loader's error
/// before <see cref="Authorize"/> is called.
/// </para>
/// </remarks>
public interface IAuthorizeResource<in TResource>
{
    /// <summary>
    /// Determines whether the actor is authorized to perform this operation
    /// against the given resource.
    /// </summary>
    /// <param name="actor">The current authenticated actor.</param>
    /// <param name="resource">The loaded resource to authorize against.</param>
    /// <returns>
    /// A success result to proceed, or a failure result (typically
    /// <see cref="Error.Forbidden"/>) to short-circuit the pipeline.
    /// </returns>
    IResult Authorize(Actor actor, TResource resource);
}