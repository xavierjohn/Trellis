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
    /// When <see langword="true"/>, a malformed <c>X-Test-Actor</c> header throws
    /// <see cref="InvalidOperationException"/> instead of falling back to the default actor.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool ThrowOnMalformedHeader { get; set; }
}