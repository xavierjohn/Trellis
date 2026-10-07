namespace Trellis.Mediator;

/// <summary>
/// Configures the resource-authorization pipeline's failure-exposure policy on a per-resource
/// basis. Register via <c>services.AddResourceAuthorization(o =&gt; o.HideExistence&lt;Incident&gt;())</c>
/// or through the <c>TrellisServiceBuilder.UseResourceAuthorization(o =&gt; ...)</c> composition slot.
/// </summary>
/// <remarks>
/// <para>
/// The default policy is <see cref="AuthFailureExposurePolicy.Propagate"/> — every command
/// returns the original <c>Error.Forbidden</c> / <c>Error.AuthenticationRequired</c> until the
/// consumer opts a resource into <see cref="AuthFailureExposurePolicy.HideAsNotFound"/>. There
/// is no global "hide everything" switch by design: hiding existence is a per-resource
/// security decision and a blanket default would silently change the wire shape of every
/// authorization failure.
/// </para>
/// <para>
/// <b>Translation scope.</b> The pipeline normalizes root <c>Error.NotFound</c>, <c>Error.Forbidden</c>, and
/// <c>Error.AuthenticationRequired</c> to the same public NotFound. Its type and identifier
/// come from the configured public resource and message, and its code/detail come only from
/// this configuration. Original metadata and causes are not copied to the public error.
/// Other direct-resource and via-leaf errors pass through unchanged. Via intermediate/owner
/// load failures retain their collapse-to-denial behavior.
/// </para>
/// <para>
/// <b>Pipeline interaction.</b> When a command implements both
/// <c>IAuthorize</c> and <c>IAuthorizeResource&lt;T&gt;</c>, the static-permission
/// <c>AuthorizationBehavior</c> runs BEFORE the resource-authorization behavior. An
/// unauthenticated caller whose request fails the static gate sees the original 401/403 — the
/// hide-as-NotFound translation never runs. Commands that need existence-hiding to apply to
/// every failure mode should omit <c>IAuthorize</c> and let
/// <see cref="AuthFailureExposurePolicy.HideAsNotFound"/> cover the resource-authorization
/// branch alone.
/// </para>
/// <para>
/// <b>Cache safety.</b> Hidden 404s can be shared across actors regardless of body differences. Protect them
/// with <c>Cache-Control: no-store</c> or <c>private</c> — a shared cache will otherwise
/// serve an unauthorized actor's synthetic 404 to a later authorized actor.
/// </para>
/// <para>
/// Normalization applies to the resource-authorization stage, not unrelated handler errors
/// or application response customization. It does not equalize response timing.
/// </para>
/// </remarks>
public sealed class ResourceAuthorizationOptions
{
    private readonly Dictionary<Type, ResourceExposureEntry> _perResource = [];

    /// <summary>
    /// Gets or sets the default exposure policy applied to resources that have no per-resource
    /// override. Defaults to <see cref="AuthFailureExposurePolicy.Propagate"/>.
    /// </summary>
    public AuthFailureExposurePolicy DefaultExposurePolicy { get; set; } =
        AuthFailureExposurePolicy.Propagate;

    /// <summary>
    /// Opt the given resource into <see cref="AuthFailureExposurePolicy.HideAsNotFound"/>.
    /// Missing resources and authorization denials receive the same public NotFound using
    /// <typeparamref name="TResource"/> as both the public type and ID extraction source.
    /// The default code and detail are used.
    /// </summary>
    /// <typeparam name="TResource">The resource type to hide. Must be a reference type
    /// because the resource-authorization behaviors already constrain
    /// <c>where TResource : class</c>.</typeparam>
    /// <returns>This options instance for chaining.</returns>
    public ResourceAuthorizationOptions HideExistence<TResource>()
        where TResource : class
        => HideExistence<TResource>(code: null, detail: null);

    /// <summary>
    /// Hides existence using fixed public metadata shared by missing and withheld resources.
    /// </summary>
    /// <typeparam name="TResource">The reference-type resource to hide.</typeparam>
    /// <param name="code">The public reason code. Null, empty, or whitespace uses the default unspecified code.</param>
    /// <param name="detail">The public detail, or null to use the standard NotFound rendering.</param>
    /// <remarks>Replaces this resource's previous configuration. Parameterless reconfiguration resets code/detail.</remarks>
    /// <returns>This options instance for chaining.</returns>
    public ResourceAuthorizationOptions HideExistence<TResource>(string? code = null, string? detail = null)
        where TResource : class
    {
        _perResource[typeof(TResource)] = new ResourceExposureEntry(
            AuthFailureExposurePolicy.HideAsNotFound,
            PublicResourceType: typeof(TResource),
            IdResourceType: typeof(TResource),
            NotFoundCode: code,
            NotFoundDetail: detail);
        return this;
    }

    /// <summary>
    /// Opt the given resource into <see cref="AuthFailureExposurePolicy.HideAsNotFound"/>
    /// using a separate public resource type for the canonical <c>NotFound</c> payload.
    /// Use this when the loader projects to an internal authorization shape that is not the
    /// canonical wire-public type (for example loading <c>OrderOwnership</c> for the
    /// authorization check while exposing <c>Order</c> as the public resource).
    /// </summary>
    /// <typeparam name="TAuthorizationResource">The resource type the loader returns (what
    /// the pipeline authorizes against).</typeparam>
    /// <typeparam name="TPublicResource">The resource type to publish in the synthetic
    /// <c>NotFound</c>'s <c>ResourceRef</c>. The pipeline also uses this type to locate an
    /// <c>IIdentifyResource&lt;TPublicResource, TId&gt;</c> implementation on the message for
    /// ID extraction, falling back to
    /// <c>IIdentifyResource&lt;TAuthorizationResource, TId&gt;</c> if the public variant is
    /// not present.</typeparam>
    /// <returns>This options instance for chaining.</returns>
    public ResourceAuthorizationOptions HideExistence<TAuthorizationResource, TPublicResource>()
        where TAuthorizationResource : class
        => HideExistence<TAuthorizationResource, TPublicResource>(code: null, detail: null);

    /// <summary>
    /// Hides a projected authorization resource using a public resource type and fixed public metadata.
    /// Missing and withheld resources receive the same public NotFound.
    /// </summary>
    /// <typeparam name="TAuthorizationResource">The reference-type resource returned by the loader.</typeparam>
    /// <typeparam name="TPublicResource">The public resource type used for the NotFound and identifier lookup.</typeparam>
    /// <param name="code">The public reason code. Null, empty, or whitespace uses the default unspecified code.</param>
    /// <param name="detail">The public detail, or null to use the standard NotFound rendering.</param>
    /// <remarks>Replaces this resource's previous configuration. Parameterless reconfiguration resets code/detail.</remarks>
    /// <returns>This options instance for chaining.</returns>
    public ResourceAuthorizationOptions HideExistence<TAuthorizationResource, TPublicResource>(
        string? code = null, string? detail = null)
        where TAuthorizationResource : class
    {
        _perResource[typeof(TAuthorizationResource)] = new ResourceExposureEntry(
            AuthFailureExposurePolicy.HideAsNotFound,
            PublicResourceType: typeof(TPublicResource),
            IdResourceType: typeof(TPublicResource),
            NotFoundCode: code,
            NotFoundDetail: detail);
        return this;
    }

    /// <summary>
    /// Explicitly opt the given resource into <see cref="AuthFailureExposurePolicy.Propagate"/>.
    /// Useful for overriding a non-<see cref="AuthFailureExposurePolicy.Propagate"/>
    /// <see cref="DefaultExposurePolicy"/> on individual resources.
    /// </summary>
    /// <typeparam name="TResource">The resource type whose failures must always propagate.</typeparam>
    /// <returns>This options instance for chaining.</returns>
    public ResourceAuthorizationOptions Propagate<TResource>()
        where TResource : class
    {
        _perResource[typeof(TResource)] = new ResourceExposureEntry(
            AuthFailureExposurePolicy.Propagate,
            PublicResourceType: typeof(TResource),
            IdResourceType: typeof(TResource));
        return this;
    }

    /// <summary>
    /// Resolves the effective exposure entry for the given authorization-resource type. Falls
    /// back to <see cref="DefaultExposurePolicy"/> with the lookup type itself acting as both
    /// public and ID source when no per-resource override is configured.
    /// </summary>
    internal ResourceExposureEntry Resolve(Type authorizationResourceType)
    {
        if (_perResource.TryGetValue(authorizationResourceType, out var entry))
            return entry;
        return new ResourceExposureEntry(
            DefaultExposurePolicy,
            PublicResourceType: authorizationResourceType,
            IdResourceType: authorizationResourceType);
    }

    /// <summary>
    /// Internal per-resource entry capturing the effective exposure policy plus the public and
    /// ID-source resource types for the synthetic <c>NotFound</c>. The projection overload
    /// (<see cref="HideExistence{TAuthorizationResource, TPublicResource}()"/>) decouples
    /// <c>PublicResourceType</c> and <c>IdResourceType</c> from the lookup-type so the public
    /// payload can reference the consumer-facing aggregate even when the loader returned a
    /// projection.
    /// </summary>
    internal readonly record struct ResourceExposureEntry(
        AuthFailureExposurePolicy Policy,
        Type PublicResourceType,
        Type IdResourceType,
        string? NotFoundCode = null,
        string? NotFoundDetail = null);
}