namespace Trellis.Asp.Authorization.Tests;

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis.Authorization;
using Trellis.Testing;

/// <summary>
/// Tests for <see cref="EasyAuthClaimsActorProvider"/> and its DI registration
/// (<see cref="ServiceCollectionExtensions.AddEasyAuthActorProvider"/> /
/// <see cref="EasyAuthAuthenticationExtensions.AddEasyAuth(AuthenticationBuilder)"/>).
/// </summary>
public class EasyAuthClaimsActorProviderTests
{
    private static EasyAuthClaimsActorProvider CreateProvider(
        ClaimsPrincipal user,
        ClaimsActorOptions? options = null)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        return new EasyAuthClaimsActorProvider(accessor, Options.Create(options ?? new ClaimsActorOptions()));
    }

    private static ClaimsPrincipal AuthenticatedUser(params Claim[] claims)
        => new(new ClaimsIdentity(claims, EasyAuthDefaults.AuthenticationScheme));

    [Fact]
    public void VaryByHeaders_NamesEasyAuthPrincipalHeaders_NotAuthorization()
    {
        // An Easy Auth actor is derived from the platform principal headers, not Authorization.
        // Reusing the base ["Authorization"] would let a cache serve actor A's response to B.
        var provider = CreateProvider(new ClaimsPrincipal());

        provider.VaryByHeaders.Should().BeEquivalentTo(
        [
            EasyAuthDefaults.PrincipalHeaderName,
            EasyAuthDefaults.PrincipalIdHeaderName,
            EasyAuthDefaults.PrincipalNameHeaderName,
            EasyAuthDefaults.PrincipalIdpHeaderName,
        ]);
    }

    [Fact]
    public async Task GetCurrentActor_ResolvesIdAndPermissions_FromConfiguredClaims()
    {
        var user = AuthenticatedUser(
            new Claim("oid", "user-1"),
            new Claim("roles", "orders:read"),
            new Claim("roles", "orders:write"));
        var options = new ClaimsActorOptions { ActorIdClaim = "oid", PermissionsClaim = "roles" };

        var actor = (await CreateProvider(user, options)
            .GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("user-1");
        actor.Permissions.Should().BeEquivalentTo(["orders:read", "orders:write"]);
    }

    [Fact]
    public async Task GetCurrentActor_FallbackNameIdentifier_ResolvesWithDefaultSubOption()
    {
        // The handler's -ID fallback emits ClaimTypes.NameIdentifier; the default ActorIdClaim
        // "sub" must still resolve it via the base provider's short<->long claim fallback.
        var user = AuthenticatedUser(new Claim(ClaimTypes.NameIdentifier, "user-42"));

        var actor = (await CreateProvider(user)
            .GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("user-42");
    }

    [Fact]
    public async Task GetCurrentActor_NoAuthenticatedIdentity_ReturnsNone()
    {
        var provider = CreateProvider(new ClaimsPrincipal(new ClaimsIdentity()));

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void AddEasyAuthActorProvider_RegistersScopedIActorProvider()
    {
        var services = new ServiceCollection();

        services.AddEasyAuthActorProvider();

        var descriptor = services.Single(d => d.ServiceType == typeof(IActorProvider));
        descriptor.ImplementationType.Should().Be<EasyAuthClaimsActorProvider>();
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddEasyAuthActorProvider_WithConfigure_AppliesOptions()
    {
        var services = new ServiceCollection();
        services.AddEasyAuthActorProvider(opts => opts.ActorIdClaim = "oid");
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<ClaimsActorOptions>>().Value.ActorIdClaim.Should().Be("oid");
    }

    [Fact]
    public void AddEasyAuthActorProvider_after_AddClaimsActorProvider_leaves_single_descriptor()
    {
        var services = new ServiceCollection();
        services.AddClaimsActorProvider();
        services.AddEasyAuthActorProvider();

        services.Where(d => d.ServiceType == typeof(IActorProvider)).Should().HaveCount(1);
    }

    [Fact]
    public async Task AddEasyAuth_RegistersAuthenticationScheme()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication().AddEasyAuth();
        var provider = services.BuildServiceProvider();

        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = await schemeProvider.GetSchemeAsync(EasyAuthDefaults.AuthenticationScheme);

        scheme.Should().NotBeNull();
        scheme!.HandlerType.Should().Be<EasyAuthAuthenticationHandler>();
    }
}
