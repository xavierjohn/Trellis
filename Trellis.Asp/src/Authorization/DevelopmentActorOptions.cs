namespace Trellis.Asp.Authorization;

using Trellis.Authorization;

/// <summary>
/// Configuration options for <see cref="DevelopmentActorProvider"/>.
/// Controls the fallback actor used when no <c>X-Test-Actor</c> header is present,
/// and error handling for malformed headers.
/// </summary>
public sealed class DevelopmentActorOptions
{
    /// <summary>
    /// The unique identifier for the default fallback actor.
    /// Used when no <c>X-Test-Actor</c> header is present in the request.
    /// Defaults to <c>"development"</c>.
    /// </summary>
    public string DefaultActorId { get; set; } = "development";

    /// <summary>
    /// The permissions granted to the default fallback actor.
    /// Defaults to an empty set — override to grant permissions when the header is absent.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddDevelopmentActorProvider(options =>
    /// {
    ///     options.DefaultPermissions = new HashSet&lt;string&gt;
    ///     {
    ///         "orders:create", "orders:read", "orders:read-all"
    ///     };
    /// });
    /// </code>
    /// </example>
    public IReadOnlySet<string> DefaultPermissions { get; set; } = new HashSet<string>();

    /// <summary>
    /// When <see langword="true"/> (the default), a malformed <c>X-Test-Actor</c> header throws
    /// <see cref="InvalidOperationException"/> instead of falling back to the default actor.
    /// </summary>
    /// <remarks>
    /// A malformed header is a developer error and is treated distinctly from an <em>absent</em>
    /// header (which intentionally yields the configured default actor). Rejecting it by default
    /// avoids silently granting the default actor's permissions — a privilege elevation when
    /// <see cref="DefaultPermissions"/> is non-empty. Set to <see langword="false"/> to restore the
    /// lenient fall-back-to-default behavior.
    /// </remarks>
    public bool ThrowOnMalformedHeader { get; set; } = true;
}