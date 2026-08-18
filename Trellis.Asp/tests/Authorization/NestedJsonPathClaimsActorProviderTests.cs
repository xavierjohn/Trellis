namespace Trellis.Asp.Authorization.Tests;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trellis.Testing;

/// <summary>
/// Tests for <see cref="NestedJsonPathClaimsActorProvider"/>, which maps an identity
/// provider's nested-JSON claim shape (Auth0 <c>app_metadata.roles</c>, Azure B2C
/// <c>extension_*</c>, Okta nested claims) onto <see cref="Actor.Id"/> and
/// <see cref="Actor.Permissions"/> via a dotted JSON path.
/// </summary>
[Collection("ClaimsActorProviderDiagnostics")]
public class NestedJsonPathClaimsActorProviderTests
{
    private static NestedJsonPathClaimsActorProvider CreateProvider(
        ClaimsPrincipal user,
        NestedJsonPathClaimsActorOptions options,
        ILogger<NestedJsonPathClaimsActorProvider>? logger = null)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var opts = Options.Create(options);
        return new NestedJsonPathClaimsActorProvider(accessor, opts, logger);
    }

    private static IHostBuilder CreateNestedJsonPathClaimsActorProviderHost(
        Action<NestedJsonPathClaimsActorOptions>? configureOptions = null) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(s => s.AddNestedJsonPathClaimsActorProvider(configureOptions));

    private static ClaimsPrincipal AuthenticatedUser(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Bearer"));

    // ---------- Auth0-style: app_metadata.roles as an array of strings ----------

    [Fact]
    public async Task GetCurrentActorAsync_Auth0RolesArray_ResolvesActorIdAndPermissions()
    {
        var user = AuthenticatedUser(
            new Claim("sub", "auth0|abc"),
            new Claim("app_metadata", """
                {"user_id":"auth0|abc","roles":["orders:read","orders:write"]}
                """));

        var provider = CreateProvider(user, new NestedJsonPathClaimsActorOptions
        {
            ActorIdClaim = "sub",
            ContainerClaim = "app_metadata",
            PermissionsPath = "roles",
        });

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("auth0|abc");
        actor.Permissions.Should().BeEquivalentTo(["orders:read", "orders:write"]);
    }

    [Fact]
    public async Task GetCurrentActorAsync_Auth0RolesArray_ActorIdPath_ResolvesFromContainerJson()
    {
        var user = AuthenticatedUser(
            new Claim("sub", "auth0|abc"),
            new Claim("app_metadata", """
                {"user_id":"auth0|xyz","roles":["orders:read"]}
                """));

        var provider = CreateProvider(user, new NestedJsonPathClaimsActorOptions
        {
            ContainerClaim = "app_metadata",
            ActorIdPath = "user_id",
            PermissionsPath = "roles",
        });

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("auth0|xyz",
            "ActorIdPath traversal should resolve from the container JSON, not the flat 'sub' claim");
    }

    // ---------- Roles-as-object: { "orders:read": true, "orders:write": true } ----------

    [Fact]
    public async Task GetCurrentActorAsync_RolesAsObject_FlattensPropertyNamesAsPermissions()
    {
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("app_metadata", """
                {"roles":{"orders:read":true,"orders:write":true,"customers:read":true}}
                """));

        var provider = CreateProvider(user, new NestedJsonPathClaimsActorOptions
        {
            ActorIdClaim = "sub",
            ContainerClaim = "app_metadata",
            PermissionsPath = "roles",
        });

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Permissions.Should().BeEquivalentTo(["orders:read", "orders:write", "customers:read"]);
    }

    // ---------- Deep traversal: a.b.c ----------

    [Fact]
    public async Task GetCurrentActorAsync_DeepDottedPath_TraversesEverySegment()
    {
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("custom_claims", """
                {"app":{"realm":{"roles":["orders:read"]}}}
                """));

        var provider = CreateProvider(user, new NestedJsonPathClaimsActorOptions
        {
            ActorIdClaim = "sub",
            ContainerClaim = "custom_claims",
            PermissionsPath = "app.realm.roles",
        });

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Permissions.Should().BeEquivalentTo(["orders:read"]);
    }

    // ---------- Fallbacks ----------

    [Fact]
    public async Task GetCurrentActorAsync_PathMissesInsideContainer_FallsBackToFlatPermissions()
    {
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("app_metadata", """{"other":"value"}"""),
            new Claim("permissions", "orders:read"));

        var provider = CreateProvider(user, new NestedJsonPathClaimsActorOptions
        {
            ActorIdClaim = "sub",
            ContainerClaim = "app_metadata",
            PermissionsPath = "roles",  // not present in app_metadata
            PermissionsClaim = "permissions",
        });

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Permissions.Should().BeEquivalentTo(["orders:read"],
            "the nested path missed but the flat PermissionsClaim must still resolve");
    }

    [Fact]
    public async Task GetCurrentActorAsync_NoContainerClaim_FallsBackEntirelyToBaseClass()
    {
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("permissions", "orders:read"));

        var provider = CreateProvider(user, new NestedJsonPathClaimsActorOptions
        {
            ActorIdClaim = "sub",
            ContainerClaim = "app_metadata",  // absent on this token
            PermissionsPath = "roles",
        });

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("user-1");
        actor.Permissions.Should().BeEquivalentTo(["orders:read"]);
    }

    [Fact]
    public async Task GetCurrentActorAsync_MalformedContainerJson_FallsBackAndLogsWarningOnce()
    {
        NestedJsonPathClaimsActorProvider.ResetDiagnosticThrottlesForTests();
        var entries = new List<(LogLevel Level, EventId EventId, string Message)>();
        var logger = new FakeLogger(entries);

        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("app_metadata", "{not valid json"),
            new Claim("permissions", "orders:read"));

        var provider = CreateProvider(
            user,
            new NestedJsonPathClaimsActorOptions
            {
                ActorIdClaim = "sub",
                ContainerClaim = "app_metadata",
                PermissionsPath = "roles",
                PermissionsClaim = "permissions",
            },
            logger);

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Permissions.Should().BeEquivalentTo(["orders:read"],
            "malformed JSON must trigger a fall-through to the flat-claim resolution");
        entries.Should().ContainSingle(e => e.EventId.Id == 4)
            .Which.Level.Should().Be(LogLevel.Warning);

        // Re-invoke: warning is throttled.
        await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);
        entries.Count(e => e.EventId.Id == 4).Should().Be(1);
    }

    [Fact]
    public async Task GetCurrentActorAsync_NoConfiguredPaths_BehavesLikeBaseProvider()
    {
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("permissions", "orders:read"));

        var provider = CreateProvider(user, new NestedJsonPathClaimsActorOptions());

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("user-1");
        actor.Permissions.Should().BeEquivalentTo(["orders:read"]);
    }

    public static TheoryData<string, Action<NestedJsonPathClaimsActorOptions>> NestedPathOptionsWithoutContainerClaim() => new()
    {
        { nameof(NestedJsonPathClaimsActorOptions.ActorIdPath), o => o.ActorIdPath = "user_id" },
        { nameof(NestedJsonPathClaimsActorOptions.PermissionsPath), o => o.PermissionsPath = "roles" },
        // Whitespace-only ContainerClaim is functionally the same as missing — the provider
        // would silently fall back to flat-claim resolution. Validation must treat it as missing
        // (regression test for the IsNullOrEmpty → IsNullOrWhiteSpace fix in PR #567 review).
        {
            nameof(NestedJsonPathClaimsActorOptions.ActorIdPath),
            o => { o.ContainerClaim = "   "; o.ActorIdPath = "user_id"; }
        },
    };

    [Fact]
    public async Task AddNestedJsonPathClaimsActorProvider_DefaultOptions_HostStartsSuccessfully()
    {
        using var host = CreateNestedJsonPathClaimsActorProviderHost().Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var options = host.Services.GetRequiredService<IOptions<NestedJsonPathClaimsActorOptions>>().Value;
        options.ContainerClaim.Should().BeEmpty();
    }

    [Fact]
    public async Task AddNestedJsonPathClaimsActorProvider_ValidNestedOptions_HostStartsSuccessfully()
    {
        using var host = CreateNestedJsonPathClaimsActorProviderHost(o =>
        {
            o.ActorIdClaim = "sub";
            o.ContainerClaim = "app_metadata";
            o.ActorIdPath = "user_id";
            o.PermissionsPath = "roles";
        }).Build();

        await host.StartAsync(TestContext.Current.CancellationToken);

        var options = host.Services.GetRequiredService<IOptions<NestedJsonPathClaimsActorOptions>>().Value;
        options.ContainerClaim.Should().Be("app_metadata");
        options.ActorIdPath.Should().Be("user_id");
        options.PermissionsPath.Should().Be("roles");
    }

    [Theory]
    [MemberData(nameof(NestedPathOptionsWithoutContainerClaim))]
    public async Task AddNestedJsonPathClaimsActorProvider_PathConfiguredWithoutContainerClaim_ThrowsOptionsValidationException(
        string pathPropertyName,
        Action<NestedJsonPathClaimsActorOptions> configureOptions)
    {
        var act = async () =>
        {
            using var host = CreateNestedJsonPathClaimsActorProviderHost(configureOptions).Build();
            await host.StartAsync(TestContext.Current.CancellationToken);
        };

        await act.Should().ThrowAsync<OptionsValidationException>()
            .WithMessage($"*{nameof(NestedJsonPathClaimsActorOptions.ContainerClaim)}*must be set*{pathPropertyName}*");
    }

    [Fact]
    public void Constructor_PermissionsPathSetWithoutContainerClaim_Throws()
    {
        // Misconfiguration guard: setting PermissionsPath without ContainerClaim would
        // silently fall back to flat-claim resolution, ignoring the path the consumer
        // expected to traverse. Throw at construction so the misconfiguration is caught at
        // first request rather than silently silently 403-ing requests in production.
        var act = () => CreateProvider(
            AuthenticatedUser(new Claim("sub", "user-1")),
            new NestedJsonPathClaimsActorOptions
            {
                ActorIdClaim = "sub",
                ContainerClaim = "",  // intentionally empty
                PermissionsPath = "roles",
            });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ContainerClaim must be set*");
    }

    [Fact]
    public void Constructor_ActorIdPathSetWithoutContainerClaim_Throws()
    {
        var act = () => CreateProvider(
            AuthenticatedUser(new Claim("sub", "user-1")),
            new NestedJsonPathClaimsActorOptions
            {
                ActorIdPath = "user_id",
                ContainerClaim = "",  // intentionally empty
            });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ContainerClaim must be set*");
    }

    [Fact]
    public async Task GetCurrentActorAsync_PermissionsPathResolvesToScalarNumber_EmitsEmptyPermissionsWarning()
    {
        ClaimsActorProvider.ResetDiagnosticThrottlesForTests();
        NestedJsonPathClaimsActorProvider.ResetDiagnosticThrottlesForTests();
        var entries = new List<(LogLevel Level, EventId EventId, string Message)>();
        var logger = new FakeLogger(entries);

        // Terminal path element is a number — TraverseToStringSet returns empty. Without the
        // diagnostic forward-call to the base, the consumer would see a silent 403.
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("app_metadata", """{"roles":42}"""));

        var provider = CreateProvider(
            user,
            new NestedJsonPathClaimsActorOptions
            {
                ActorIdClaim = "sub",
                ContainerClaim = "app_metadata",
                PermissionsPath = "roles",
                // No flat PermissionsClaim fallback configured — the path itself produced empty,
                // so the empty-permissions diagnostic on the base must fire.
                PermissionsClaim = "",
            },
            logger);

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Permissions.Should().BeEmpty();
        entries.Should().ContainSingle(e => e.EventId.Id == 2)
            .Which.Level.Should().Be(LogLevel.Warning,
            "the override must forward to base.MaybeEmitClaimShapeDiagnostics so terminal-shape misses do not silently 403");
    }

    [Fact]
    public async Task GetCurrentActorAsync_ActorIdPathMiss_UsesBaseShortLongFallback()
    {
        // Configure ActorIdClaim = "sub" but the JWT carries the long-form
        // ClaimTypes.NameIdentifier (the default when JwtBearerOptions.MapInboundClaims = true).
        // The nested fallback must route through ResolveClaimWithFallback so the long form is
        // discovered. A literal identity.FindFirst("sub") would miss and 401.
        var user = AuthenticatedUser(
            new Claim(ClaimTypes.NameIdentifier, "user-mapped"),
            new Claim("app_metadata", """{"other":"value"}"""));

        var provider = CreateProvider(user, new NestedJsonPathClaimsActorOptions
        {
            ActorIdClaim = "sub",  // not on the token literally — MapInboundClaims remapped to long form
            ContainerClaim = "app_metadata",
            ActorIdPath = "user_id",  // path miss → fallback path
            PermissionsPath = "roles",
        });

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("user-mapped",
            "the nested fallback must use ResolveClaimWithFallback (short↔long aware), not literal FindFirst");
    }

    /// <summary>
    /// Minimal fake logger that captures log entries for assertion.
    /// </summary>
    private sealed class FakeLogger(List<(LogLevel Level, EventId EventId, string Message)> entries)
        : ILogger<NestedJsonPathClaimsActorProvider>
    {
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => entries.Add((logLevel, eventId, formatter(state, exception)));

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}