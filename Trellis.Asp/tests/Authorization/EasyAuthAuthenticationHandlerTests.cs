namespace Trellis.Asp.Authorization.Tests;

using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// Tests for <see cref="EasyAuthAuthenticationHandler"/> — decoding Azure "Easy Auth"
/// principal headers into an authenticated <see cref="System.Security.Claims.ClaimsPrincipal"/>.
/// </summary>
public class EasyAuthAuthenticationHandlerTests
{
    private static string Encode(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    private static string EncodeUrl(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<AuthenticateResult> AuthenticateAsync(
        Action<HttpRequest> configureRequest,
        EasyAuthAuthenticationOptions? options = null)
    {
        var context = new DefaultHttpContext();
        configureRequest(context.Request);

        var handler = new EasyAuthAuthenticationHandler(
            new StaticOptionsMonitor(options ?? new EasyAuthAuthenticationOptions()),
            NullLoggerFactory.Instance,
            System.Text.Encodings.Web.UrlEncoder.Default);

        var scheme = new AuthenticationScheme(
            EasyAuthDefaults.AuthenticationScheme,
            displayName: null,
            handlerType: typeof(EasyAuthAuthenticationHandler));
        await handler.InitializeAsync(scheme, context);
        return await handler.AuthenticateAsync();
    }

    [Fact]
    public async Task Authenticate_ValidPrincipalHeader_ProducesAuthenticatedPrincipalWithClaims()
    {
        var json = """
        {
          "auth_typ": "aad",
          "name_typ": "name",
          "role_typ": "roles",
          "claims": [
            { "typ": "sub", "val": "user-1" },
            { "typ": "name", "val": "Ada" },
            { "typ": "roles", "val": "orders:read" }
          ]
        }
        """;

        var result = await AuthenticateAsync(req =>
            req.Headers[EasyAuthDefaults.PrincipalHeaderName] = Encode(json));

        result.Succeeded.Should().BeTrue();
        result.Principal!.Identity!.IsAuthenticated.Should().BeTrue();
        result.Principal.Identity.AuthenticationType.Should().Be("aad");
        result.Principal.FindFirst("sub")!.Value.Should().Be("user-1");
    }

    [Fact]
    public async Task Authenticate_ValidPrincipalHeader_HonorsNameAndRoleType()
    {
        var json = """
        {
          "auth_typ": "aad",
          "name_typ": "name",
          "role_typ": "roles",
          "claims": [
            { "typ": "name", "val": "Ada" },
            { "typ": "roles", "val": "orders:read" }
          ]
        }
        """;

        var result = await AuthenticateAsync(req =>
            req.Headers[EasyAuthDefaults.PrincipalHeaderName] = Encode(json));

        // name_typ / role_typ drive Identity.Name and role checks.
        result.Principal!.Identity!.Name.Should().Be("Ada");
        result.Principal.IsInRole("orders:read").Should().BeTrue();
    }

    [Fact]
    public async Task Authenticate_AuthTypMissing_UsesSchemeName_AndIsAuthenticated()
    {
        var json = """
        { "claims": [ { "typ": "sub", "val": "user-1" } ] }
        """;

        var result = await AuthenticateAsync(req =>
            req.Headers[EasyAuthDefaults.PrincipalHeaderName] = Encode(json));

        result.Succeeded.Should().BeTrue();
        result.Principal!.Identity!.IsAuthenticated.Should().BeTrue();
        result.Principal.Identity.AuthenticationType.Should().Be(EasyAuthDefaults.AuthenticationScheme);
    }

    [Fact]
    public async Task Authenticate_NoEasyAuthHeaders_ReturnsNoResult()
    {
        var result = await AuthenticateAsync(_ => { });

        result.None.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().BeNull();
    }

    [Fact]
    public async Task Authenticate_InvalidBase64_FailsClosed()
    {
        var result = await AuthenticateAsync(req =>
            req.Headers[EasyAuthDefaults.PrincipalHeaderName] = "!!!not-base64!!!");

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
    }

    [Fact]
    public async Task Authenticate_InvalidJson_FailsClosed()
    {
        var result = await AuthenticateAsync(req =>
            req.Headers[EasyAuthDefaults.PrincipalHeaderName] = Encode("not json {{{"));

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
    }

    [Fact]
    public async Task Authenticate_PrincipalWithoutClaimsArray_FailsClosed()
    {
        var result = await AuthenticateAsync(req =>
            req.Headers[EasyAuthDefaults.PrincipalHeaderName] = Encode("""{ "auth_typ": "aad" }"""));

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
    }

    [Fact]
    public async Task Authenticate_FallbackIdHeader_ProducesAuthenticatedPrincipal()
    {
        var result = await AuthenticateAsync(req =>
        {
            req.Headers[EasyAuthDefaults.PrincipalIdHeaderName] = "user-42";
            req.Headers[EasyAuthDefaults.PrincipalNameHeaderName] = "ada@example.com";
            req.Headers[EasyAuthDefaults.PrincipalIdpHeaderName] = "aad";
        });

        result.Succeeded.Should().BeTrue();
        result.Principal!.Identity!.IsAuthenticated.Should().BeTrue();
        result.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value.Should().Be("user-42");
        result.Principal.Identity.AuthenticationType.Should().Be("aad");
    }

    [Fact]
    public async Task Authenticate_Base64UrlEncodedPrincipal_Authenticates()
    {
        // Easy Auth emits standard base64, but the handler must also accept base64url
        // (URL-safe alphabet, unpadded) — which Convert.FromBase64String alone would reject.
        var json = """{ "auth_typ": "aad", "claims": [ { "typ": "sub", "val": "user-1" } ] }""";

        var result = await AuthenticateAsync(req =>
            req.Headers[EasyAuthDefaults.PrincipalHeaderName] = EncodeUrl(json));

        result.Succeeded.Should().BeTrue();
        result.Principal!.FindFirst("sub")!.Value.Should().Be("user-1");
    }

    private sealed class StaticOptionsMonitor(EasyAuthAuthenticationOptions value)
        : IOptionsMonitor<EasyAuthAuthenticationOptions>
    {
        public EasyAuthAuthenticationOptions CurrentValue => value;

        public EasyAuthAuthenticationOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<EasyAuthAuthenticationOptions, string?> listener) => null;
    }
}
