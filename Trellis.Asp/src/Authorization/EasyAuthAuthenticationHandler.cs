namespace Trellis.Asp.Authorization;

using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Authentication handler that turns Azure App Service / Container Apps built-in authentication
/// ("Easy Auth") principal headers into an authenticated <see cref="ClaimsPrincipal"/> on
/// <c>HttpContext.User</c>. Once the principal is on the context, the standard Trellis actor
/// providers (<see cref="ClaimsActorProvider"/> / <see cref="EasyAuthClaimsActorProvider"/>)
/// map its claims to an <c>Actor</c> — no bespoke header parsing in the actor layer.
/// </summary>
/// <remarks>
/// <para>
/// Decodes <see cref="EasyAuthDefaults.PrincipalHeaderName"/>
/// (<c>X-MS-CLIENT-PRINCIPAL</c>) — base64 JSON of the shape
/// <c>{ "auth_typ": "...", "name_typ": "...", "role_typ": "...", "claims": [ { "typ": "...", "val": "..." } ] }</c>
/// — honoring <c>name_typ</c> / <c>role_typ</c> so <c>ClaimsPrincipal.Identity.Name</c> and
/// role checks resolve. When the principal header is absent it falls back to the
/// <c>-ID</c> / <c>-NAME</c> convenience headers. When no Easy Auth header is present it
/// returns <see cref="AuthenticateResult.NoResult"/> (anonymous); a malformed principal header
/// fails closed via <see cref="AuthenticateResult.Fail(string)"/> rather than trusting partial
/// data.
/// </para>
/// <para>
/// <b>Trust precondition:</b> see <see cref="EasyAuthDefaults"/>. Register only when the app is
/// reachable exclusively through the Easy Auth front end.
/// </para>
/// </remarks>
public sealed class EasyAuthAuthenticationHandler : AuthenticationHandler<EasyAuthAuthenticationOptions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EasyAuthAuthenticationHandler"/> class.
    /// </summary>
    public EasyAuthAuthenticationHandler(
        IOptionsMonitor<EasyAuthAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var principalHeader = Request.Headers[EasyAuthDefaults.PrincipalHeaderName].ToString();
        if (!string.IsNullOrEmpty(principalHeader))
            return Task.FromResult(DecodePrincipalHeader(principalHeader));

        var id = Request.Headers[EasyAuthDefaults.PrincipalIdHeaderName].ToString();
        var name = Request.Headers[EasyAuthDefaults.PrincipalNameHeaderName].ToString();
        if (!string.IsNullOrEmpty(id) || !string.IsNullOrEmpty(name))
        {
            var idp = Request.Headers[EasyAuthDefaults.PrincipalIdpHeaderName].ToString();
            return Task.FromResult(SuccessFromFallbackHeaders(id, name, idp));
        }

        // No Easy Auth headers at all: anonymous request. Return NoResult (not Fail) so
        // anonymous-tolerant endpoints keep working and the Trellis actor pipeline maps the
        // missing actor to 401 for endpoints that require one.
        return Task.FromResult(AuthenticateResult.NoResult());
    }

    private AuthenticateResult DecodePrincipalHeader(string headerValue)
    {
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(headerValue);
        }
        catch (FormatException)
        {
            return AuthenticateResult.Fail(
                $"The '{EasyAuthDefaults.PrincipalHeaderName}' header is not valid base64.");
        }

        ClaimsIdentity? identity;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!TryBuildIdentity(document.RootElement, out identity))
                return AuthenticateResult.Fail(
                    $"The '{EasyAuthDefaults.PrincipalHeaderName}' header did not contain a usable client principal.");
        }
        catch (JsonException)
        {
            return AuthenticateResult.Fail(
                $"The '{EasyAuthDefaults.PrincipalHeaderName}' header is not valid JSON.");
        }

        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    private bool TryBuildIdentity(JsonElement root, out ClaimsIdentity identity)
    {
        identity = null!;
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (!root.TryGetProperty("claims", out var claimsElement)
            || claimsElement.ValueKind != JsonValueKind.Array)
            return false;

        var claims = new List<Claim>();
        foreach (var claimElement in claimsElement.EnumerateArray())
        {
            if (claimElement.ValueKind != JsonValueKind.Object)
                continue;

            var type = ReadString(claimElement, "typ");
            var value = ReadString(claimElement, "val");
            if (type is not null && value is not null)
                claims.Add(new Claim(type, value));
        }

        // An authenticated Easy Auth principal always carries at least one claim. Zero usable
        // claims means the payload was not a client principal — treat as malformed.
        if (claims.Count == 0)
            return false;

        var authenticationType = ReadString(root, "auth_typ");
        var nameType = ReadString(root, "name_typ");
        var roleType = ReadString(root, "role_typ");

        identity = new ClaimsIdentity(
            claims,
            string.IsNullOrEmpty(authenticationType) ? Scheme.Name : authenticationType,
            string.IsNullOrEmpty(nameType) ? ClaimTypes.Name : nameType,
            string.IsNullOrEmpty(roleType) ? ClaimTypes.Role : roleType);
        return true;
    }

    private AuthenticateResult SuccessFromFallbackHeaders(string? id, string? name, string? idp)
    {
        var claims = new List<Claim>();
        if (!string.IsNullOrEmpty(id))
            claims.Add(new Claim(ClaimTypes.NameIdentifier, id));
        if (!string.IsNullOrEmpty(name))
            claims.Add(new Claim(ClaimTypes.Name, name));
        if (!string.IsNullOrEmpty(idp))
            claims.Add(new Claim("idp", idp));

        var identity = new ClaimsIdentity(
            claims,
            string.IsNullOrEmpty(idp) ? Scheme.Name : idp,
            ClaimTypes.Name,
            ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
