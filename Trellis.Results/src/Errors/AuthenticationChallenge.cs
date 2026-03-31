namespace Trellis;

using System.Text;

/// <summary>
/// Represents an RFC 9110 §11.3 authentication challenge for the <c>WWW-Authenticate</c> response header.
/// </summary>
/// <remarks>
/// <para>
/// HTTP 401 responses must include a <c>WWW-Authenticate</c> header with one or more challenges
/// that indicate the authentication scheme(s) the server supports (RFC 9110 §11.6.1).
/// Each challenge consists of a scheme name and either a token68 credential or a set of
/// <c>key="value"</c> auth-params.
/// </para>
/// <para>
/// Use the convenience factories (<see cref="Bearer"/>, <see cref="Basic"/>) for common schemes,
/// or <see cref="Create"/> / <see cref="CreateWithToken68"/> for custom schemes.
/// Format for HTTP headers using <see cref="ToHeaderValue"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var bearer = AuthenticationChallenge.Bearer(realm: "api", scope: "read write");
/// var basic  = AuthenticationChallenge.Basic(realm: "My API");
/// var custom = AuthenticationChallenge.Create("CustomScheme");
///
/// // Use in error creation:
/// Error.Unauthorized("Token expired", new[] { bearer })
/// </code>
/// </example>
public sealed class AuthenticationChallenge
{
    /// <summary>
    /// Gets the authentication scheme name (e.g., "Bearer", "Basic").
    /// </summary>
    public string Scheme { get; }

    /// <summary>
    /// Gets the optional token68-format credential. Mutually exclusive with <see cref="Parameters"/>.
    /// </summary>
    public string? Token68 { get; }

    /// <summary>
    /// Gets the optional authentication parameters (realm, scope, error, etc.).
    /// Mutually exclusive with <see cref="Token68"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; }

    private AuthenticationChallenge(string scheme, string? token68, IReadOnlyDictionary<string, string>? parameters)
    {
        Scheme = scheme;
        Token68 = token68;
        Parameters = parameters;
    }

    /// <summary>
    /// Creates a Bearer authentication challenge (RFC 6750).
    /// </summary>
    /// <param name="realm">The protection space identifier.</param>
    /// <param name="scope">The access scope(s) required, space-delimited.</param>
    /// <param name="error">The error code (e.g., "invalid_token").</param>
    /// <param name="errorDescription">A human-readable explanation of the error.</param>
    /// <returns>A Bearer <see cref="AuthenticationChallenge"/>.</returns>
    public static AuthenticationChallenge Bearer(
        string? realm = null,
        string? scope = null,
        string? error = null,
        string? errorDescription = null)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (realm is not null)
            parameters["realm"] = realm;
        if (scope is not null)
            parameters["scope"] = scope;
        if (error is not null)
            parameters["error"] = error;
        if (errorDescription is not null)
            parameters["error_description"] = errorDescription;

        return new AuthenticationChallenge(
            "Bearer",
            null,
            parameters.Count > 0 ? parameters.AsReadOnly() : null);
    }

    /// <summary>
    /// Creates a Basic authentication challenge (RFC 7617).
    /// </summary>
    /// <param name="realm">The protection space identifier.</param>
    /// <returns>A Basic <see cref="AuthenticationChallenge"/>.</returns>
    public static AuthenticationChallenge Basic(string? realm = null)
    {
        if (realm is null)
            return new AuthenticationChallenge("Basic", null, null);

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["realm"] = realm
        };

        return new AuthenticationChallenge("Basic", null, parameters.AsReadOnly());
    }

    /// <summary>
    /// Creates an authentication challenge with the specified scheme and optional parameters.
    /// </summary>
    /// <param name="scheme">The authentication scheme name.</param>
    /// <param name="parameters">Optional authentication parameters.</param>
    /// <returns>A new <see cref="AuthenticationChallenge"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="scheme"/> is null or whitespace.</exception>
    public static AuthenticationChallenge Create(string scheme, IReadOnlyDictionary<string, string>? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        return new AuthenticationChallenge(scheme, null, parameters);
    }

    /// <summary>
    /// Creates an authentication challenge with the specified scheme and a token68 credential.
    /// </summary>
    /// <param name="scheme">The authentication scheme name.</param>
    /// <param name="token68">The token68-format credential value.</param>
    /// <returns>A new <see cref="AuthenticationChallenge"/> with a token68 value.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="scheme"/> or <paramref name="token68"/> is null or whitespace.
    /// </exception>
    public static AuthenticationChallenge CreateWithToken68(string scheme, string token68)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(token68);
        return new AuthenticationChallenge(scheme, token68, null);
    }

    /// <summary>
    /// Formats this challenge as an RFC 9110 §11.2 compliant <c>WWW-Authenticate</c> header value.
    /// </summary>
    /// <returns>The formatted header value string.</returns>
    /// <remarks>
    /// <para>Format: <c>scheme [token68 | #auth-param]</c></para>
    /// <para>Auth params are formatted as <c>key="value"</c> pairs separated by <c>, </c>.</para>
    /// </remarks>
    public string ToHeaderValue()
    {
        if (Token68 is not null)
            return $"{Scheme} {Token68}";

        if (Parameters is null or { Count: 0 })
            return Scheme;

        var sb = new StringBuilder();
        sb.Append(Scheme);
        sb.Append(' ');

        var first = true;
        foreach (var kvp in Parameters)
        {
            if (!first)
                sb.Append(", ");

            sb.Append(kvp.Key);
            sb.Append("=\"");
            sb.Append(kvp.Value);
            sb.Append('"');
            first = false;
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public override string ToString() => ToHeaderValue();
}
