namespace Trellis.Asp.Authorization.Tests;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trellis.Testing;

/// <summary>
/// Tests for <see cref="ClaimsActorProvider"/> — the generic OIDC/JWT claims-based actor provider.
/// </summary>
[Collection("ClaimsActorProviderDiagnostics")]
public class ClaimsActorProviderTests
{
    private static ClaimsActorProvider CreateProvider(
        ClaimsPrincipal user,
        ClaimsActorOptions? options = null,
        ILogger<ClaimsActorProvider>? logger = null)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var opts = Options.Create(options ?? new ClaimsActorOptions());
        return new ClaimsActorProvider(accessor, opts, logger);
    }

    private static ClaimsPrincipal AuthenticatedUser(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "Bearer");
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Minimal fake logger that captures log entries for assertion. Mirrors the helper in
    /// Trellis.Mediator's LoggingBehaviorTests; copied locally to keep the test assembly
    /// dependency-free.
    /// </summary>
    private sealed class FakeLogger(List<(LogLevel Level, EventId EventId, string Message)> entries) : ILogger<ClaimsActorProvider>
    {
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => entries.Add((logLevel, eventId, formatter(state, exception)));

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    #region Default claim mapping (sub + permissions)

    [Fact]
    public async Task GetCurrentActorAsync_DefaultOptions_SetsIdFromSubClaim()
    {
        var user = AuthenticatedUser(new Claim("sub", "user-sub-123"));

        var actor = (await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("user-sub-123");
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultOptions_MapsPermissionsClaims()
    {
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("permissions", "orders:read"),
            new Claim("permissions", "orders:write"));

        var actor = (await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Permissions.Should().BeEquivalentTo(["orders:read", "orders:write"]);
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultOptions_NoPermissionsClaims_ReturnsEmptyPermissions()
    {
        var user = AuthenticatedUser(new Claim("sub", "user-1"));

        var actor = (await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Permissions.Should().BeEmpty();
    }

    #endregion

    #region MapInboundClaims fallback — JwtBearerHandler default-config compatibility

    [Fact]
    public async Task GetCurrentActorAsync_DefaultActorIdClaim_FallsBackToLongFormUrn_WhenJwtMapInboundClaimsRemappedSub()
    {
        // ASP.NET Core's JwtBearerHandler defaults to MapInboundClaims = true, which remaps
        // the RFC 7519 / OIDC short claim name "sub" onto the WS-* long-form URN
        // ClaimTypes.NameIdentifier. With the default ClaimsActorOptions.ActorIdClaim = "sub",
        // the literal lookup fails and the provider returns Maybe.None — the mediator then
        // emits 401 on every authenticated request, indistinguishable from "no token". This
        // is the most common Trellis + JwtBearer integration footgun.
        //
        // The fallback resolves the actor id from the long-form WS-* URN counterpart when
        // the configured short-form claim is not found (and vice versa) — same Postel's-law
        // robustness pattern EntraActorProvider already uses for its oid lookup.
        var user = AuthenticatedUser(new Claim(ClaimTypes.NameIdentifier, "user-sub-123"));

        var maybeActor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        maybeActor.HasValue.Should().BeTrue(
            "ClaimsActorProvider should resolve actor id via the WS-* long-form URN fallback when JwtBearerHandler's default MapInboundClaims has remapped the 'sub' claim");
        maybeActor.Unwrap().Id.Value.Should().Be("user-sub-123");
    }

    [Fact]
    public async Task GetCurrentActorAsync_CustomActorIdClaim_FallsBackToShortForm_WhenLongFormConfiguredButShortFormPresent()
    {
        // Reverse direction: consumer explicitly set ActorIdClaim to the long-form URN
        // (e.g., to match older code) but the token's identity carries the short-form claim
        // (because MapInboundClaims = false was set on JwtBearerHandler). The fallback runs
        // bidirectionally so both configurations resolve.
        var user = AuthenticatedUser(new Claim("sub", "user-sub-456"));
        var options = new ClaimsActorOptions { ActorIdClaim = ClaimTypes.NameIdentifier };

        var maybeActor = await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        maybeActor.HasValue.Should().BeTrue(
            "fallback is bidirectional — configured long-form should also try the short-form counterpart");
        maybeActor.Unwrap().Id.Value.Should().Be("user-sub-456");
    }

    [Fact]
    public async Task GetCurrentActorAsync_ActorIdClaim_PrefersLiteralMatchOverFallback_WhenBothShortAndLongFormPresent()
    {
        // When the configured claim matches a real claim on the identity, the literal match
        // wins — no fallback is consulted. Defensive case for tokens that carry both forms
        // (e.g., MapInboundClaims true + an explicit "sub" claim added by the IdP); we want
        // the user's configured intent to drive resolution.
        var user = AuthenticatedUser(
            new Claim("sub", "literal-match"),
            new Claim(ClaimTypes.NameIdentifier, "fallback-match"));

        var maybeActor = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        maybeActor.Unwrap().Id.Value.Should().Be("literal-match",
            "the literal match on the configured claim name takes precedence over the fallback");
    }

    [Fact]
    public async Task GetCurrentActorAsync_ActorIdClaim_NotInKnownMapping_DoesNotFireFallback()
    {
        // The fallback only covers the specific short↔long claim names that JwtBearerHandler's
        // DefaultInboundClaimTypeMap remaps. Arbitrary custom claim names (consumer-supplied,
        // not in the well-known JWT inbound mapping) get literal-only lookup — no fuzzy
        // matching against unrelated claims.
        var user = AuthenticatedUser(new Claim("some-other-claim", "value"));
        var options = new ClaimsActorOptions { ActorIdClaim = "tenant-custom-id" };

        var maybeActor = await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        maybeActor.HasValue.Should().BeFalse(
            "custom claim names not in the JWT well-known mapping table get literal lookup only — no fallback");
    }

    [Theory]
    [InlineData("nameid")]
    [InlineData("sub")]
    public async Task GetCurrentActorAsync_LongFormConfigured_FallsBackToAnyShortFormThatMapsToIt_NameIdentifier(string shortForm)
    {
        // Multiple short forms can map to the same long form: both "sub" and "nameid" alias
        // to ClaimTypes.NameIdentifier in the JWT inbound map. When the consumer configures
        // the long form and the token carries EITHER short variant, the fallback must try
        // every candidate short form — not just one canonical pick — otherwise the same
        // silent-401 footgun reappears for the long-form-configured branch.
        var user = AuthenticatedUser(new Claim(shortForm, $"value-{shortForm}"));
        var options = new ClaimsActorOptions { ActorIdClaim = ClaimTypes.NameIdentifier };

        var maybeActor = await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        maybeActor.HasValue.Should().BeTrue(
            $"reverse fallback for ClaimTypes.NameIdentifier must accept any of its mapped short forms — including '{shortForm}'");
        maybeActor.Unwrap().Id.Value.Should().Be($"value-{shortForm}");
    }

    [Theory]
    [InlineData("name")]
    [InlineData("unique_name")]
    public async Task GetCurrentActorAsync_LongFormConfigured_FallsBackToAnyShortFormThatMapsToIt_Name(string shortForm)
    {
        // Same coverage for the "name" / "unique_name" → ClaimTypes.Name collision pair.
        var user = AuthenticatedUser(new Claim(shortForm, $"value-{shortForm}"));
        var options = new ClaimsActorOptions { ActorIdClaim = ClaimTypes.Name };

        var maybeActor = await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        maybeActor.HasValue.Should().BeTrue(
            $"reverse fallback for ClaimTypes.Name must accept any of its mapped short forms — including '{shortForm}'");
        maybeActor.Unwrap().Id.Value.Should().Be($"value-{shortForm}");
    }

    [Theory]
    [InlineData("role")]
    [InlineData("roles")]
    public async Task GetCurrentActorAsync_LongFormConfigured_FallsBackToAnyShortFormThatMapsToIt_Role(string shortForm)
    {
        // Same coverage for the "role" / "roles" → ClaimTypes.Role collision pair.
        // Completes the matrix: every long-form key in KnownLongToShortClaimNames that has
        // more than one short-form mapping is exercised in both directions.
        var user = AuthenticatedUser(new Claim(shortForm, $"value-{shortForm}"));
        var options = new ClaimsActorOptions { ActorIdClaim = ClaimTypes.Role };

        var maybeActor = await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        maybeActor.HasValue.Should().BeTrue(
            $"reverse fallback for ClaimTypes.Role must accept any of its mapped short forms — including '{shortForm}'");
        maybeActor.Unwrap().Id.Value.Should().Be($"value-{shortForm}");
    }

    #endregion

    #region MapInboundClaims fallback — PermissionsClaim multi-valued lookup

    [Fact]
    public async Task GetCurrentActorAsync_DefaultPermissionsClaim_NotInJwtMap_HasNoFallback()
    {
        // Default PermissionsClaim = "permissions" is NOT in the JWT inbound claim map —
        // the JwtBearerHandler does not remap it. Literal-only lookup is correct here;
        // no fallback fires. Regression guard against accidentally introducing a fallback
        // for an unmapped name. We seed an unrelated mapped claim (ClaimTypes.Role) with a
        // distinctive value so that if a future change accidentally widened the fallback
        // (e.g. by treating "permissions" as a synonym for any role-like claim), this test
        // would observe the leak as an extra entry in the resulting permission set.
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("permissions", "orders:read"),
            new Claim(ClaimTypes.Role, "should-not-leak-via-permissions-fallback"));

        var actor = (await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Permissions.Should().BeEquivalentTo(["orders:read"],
            "the default 'permissions' claim is not in the JWT short<->long map, so no fallback should add ClaimTypes.Role values to the permission set");
    }

    [Fact]
    public async Task GetCurrentActorAsync_PermissionsClaim_ShortFormConfigured_FallsBackToLongForm()
    {
        // Consumer configures PermissionsClaim = "roles" (the RFC/Entra App Roles short
        // name). Under JwtBearerOptions.MapInboundClaims = true (the ASP.NET Core default),
        // the JWT "roles" claim is remapped onto ClaimTypes.Role. Without the fallback,
        // identity.FindAll("roles") finds nothing → Actor.Permissions is empty → every
        // IAuthorize command 403s on a valid token. Forward direction of the same footgun
        // that PR #498 fixed for ActorIdClaim.
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(ClaimTypes.Role, "Editor"));
        var options = new ClaimsActorOptions { PermissionsClaim = "roles" };

        var actor = (await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Permissions.Should().BeEquivalentTo(["Admin", "Editor"],
            "forward fallback: configured short-form 'roles' should also accept long-form ClaimTypes.Role claims");
    }

    [Theory]
    [InlineData("role")]
    [InlineData("roles")]
    public async Task GetCurrentActorAsync_PermissionsClaim_LongFormConfigured_FallsBackToEveryShortFormThatMapsToIt(string shortForm)
    {
        // Reverse direction. Consumer configures PermissionsClaim = ClaimTypes.Role (e.g.,
        // to match older code) and the token carries EITHER "role" or "roles" short forms.
        // Both must resolve — same multi-short-form coverage as the ActorIdClaim reverse
        // tests (collision pair "role"/"roles" → ClaimTypes.Role).
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim(shortForm, "Admin"),
            new Claim(shortForm, "Editor"));
        var options = new ClaimsActorOptions { PermissionsClaim = ClaimTypes.Role };

        var actor = (await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Permissions.Should().BeEquivalentTo(["Admin", "Editor"],
            $"reverse fallback for ClaimTypes.Role must accept any of its mapped short forms — including '{shortForm}'");
    }

    [Fact]
    public async Task GetCurrentActorAsync_PermissionsClaim_BothLiteralAndFallbackPresent_MergesAndDedupes()
    {
        // Token carries claims under BOTH the literal "roles" name AND the long-form
        // ClaimTypes.Role URN (real-world: a token issued by an IdP that emits both, or
        // a JwtBearer config with MapInboundClaims = true but additional claims added
        // post-validation that use the short form). Permissions are multi-valued — both
        // sources must contribute. The FrozenSet construction dedupes overlapping values.
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("roles", "Admin"),                // short form
            new Claim("roles", "Editor"),               // short form
            new Claim(ClaimTypes.Role, "Editor"),       // long form, duplicate of "Editor"
            new Claim(ClaimTypes.Role, "Reviewer"));    // long form, unique
        var options = new ClaimsActorOptions { PermissionsClaim = "roles" };

        var actor = (await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Permissions.Should().BeEquivalentTo(["Admin", "Editor", "Reviewer"],
            "permissions from both forms must merge into one set; duplicate values dedupe");
    }

    [Fact]
    public async Task GetCurrentActorAsync_PermissionsClaim_LongFormConfigured_BothShortVariantsContribute()
    {
        // ClaimTypes.Role is the long-form counterpart of two short-form names: "role"
        // (singular legacy) and "roles" (modern multi-valued). When PermissionsClaim is
        // configured as the long form ClaimTypes.Role and the token happens to carry
        // claims under both short variants, every variant must contribute. Real-world: an
        // IdP that emits both "role" and "roles" claims under MapInboundClaims = false.
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("role", "FromRoleSingular"),
            new Claim("roles", "FromRolesPlural"));
        var options = new ClaimsActorOptions { PermissionsClaim = ClaimTypes.Role };

        var actor = (await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Permissions.Should().BeEquivalentTo(["FromRoleSingular", "FromRolesPlural"],
            "every short form mapped to the configured long form must contribute its claims");
    }

    [Fact]
    public async Task GetCurrentActorAsync_PermissionsClaim_NotInKnownMapping_LiteralOnly()
    {
        // Custom permissions claim name not in the JWT well-known map. Literal lookup only
        // — no fuzzy matching against unrelated claims. Regression guard parallel to the
        // ActorIdClaim NotInKnownMapping test.
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("scope", "should-not-resolve"));
        var options = new ClaimsActorOptions { PermissionsClaim = "tenant-custom-permissions" };

        var actor = (await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Permissions.Should().BeEmpty(
            "custom permissions claim names not in the JWT well-known mapping table get literal-only lookup");
    }

    [Fact]
    public async Task GetCurrentActorAsync_PermissionsClaim_ShortFormConfigured_LiteralAbsent_LogsCounterpartFallback()
    {
        // Pins the documented diagnostic: when the configured PermissionsClaim resolves
        // nothing on the identity but a well-known counterpart does, the provider emits a
        // debug-level log entry naming both the configured claim and the resolved
        // counterpart. This is the operator-facing signal for the silent-403 footgun
        // (PermissionsClaim = "roles" against an identity carrying ClaimTypes.Role under
        // MapInboundClaims = true). Without this assertion the diagnostic could regress
        // while every permission-resolution test still passes.
        var entries = new List<(LogLevel Level, EventId EventId, string Message)>();
        var logger = new FakeLogger(entries);
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim(ClaimTypes.Role, "Admin"));
        var options = new ClaimsActorOptions { PermissionsClaim = "roles" };

        _ = (await CreateProvider(user, options, logger).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Debug
            && e.Message.Contains("'roles'")
            && e.Message.Contains($"'{ClaimTypes.Role}'"),
            "the fallback must emit one debug entry naming the configured claim ('roles') and the resolved counterpart (ClaimTypes.Role) so operators can spot the MapInboundClaims = true footgun");
    }

    [Fact]
    public async Task GetCurrentActorAsync_PermissionsClaim_LiteralPresent_DoesNotLogFallback()
    {
        // Negative pin for the same diagnostic: when the configured PermissionsClaim
        // resolves literally, no fallback log fires, even if a counterpart claim is also
        // present and contributes to the merged set. Logging on every counterpart match
        // would flood normal operation; we only want the silent-403-likely case to log.
        var entries = new List<(LogLevel Level, EventId EventId, string Message)>();
        var logger = new FakeLogger(entries);
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("roles", "Admin"),
            new Claim(ClaimTypes.Role, "Editor"));
        var options = new ClaimsActorOptions { PermissionsClaim = "roles" };

        _ = (await CreateProvider(user, options, logger).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        entries.Should().BeEmpty(
            "no fallback log should fire when the configured claim resolves literally, even if a counterpart also contributes");
    }

    #endregion

    #region MapInboundClaims fallback — extended Microsoft-identity short↔long mappings

    /// <summary>
    /// Table-driven coverage for every extended short↔long entry added beyond the RFC 7519 /
    /// OIDC standard subset. Each case configures <see cref="ClaimsActorOptions.ActorIdClaim"/>
    /// to the short form, supplies the long form on the identity (the
    /// <c>MapInboundClaims = true</c> shape), and asserts the fallback resolves. The dataset
    /// pins the literal long-form URN strings on the source table — a typo or unintended
    /// edit fails here rather than silently disabling the fallback in production. Also
    /// includes the reverse direction (configured long form, identity carrying the short).
    /// </summary>
    public static TheoryData<string, string> ExtendedShortLongPairs() => new()
    {
        // Forward direction will be tested with these (configured = Item1, identity-claim = Item2).
        // Reverse direction with the same pair (configured = Item2, identity-claim = Item1).
        { "upn", ClaimTypes.Upn },
        { "oid", "http://schemas.microsoft.com/identity/claims/objectidentifier" },
        { "tid", "http://schemas.microsoft.com/identity/claims/tenantid" },
        { "idp", "http://schemas.microsoft.com/identity/claims/identityprovider" },
        { "acr", "http://schemas.microsoft.com/claims/authnclassreference" },
        { "amr", "http://schemas.microsoft.com/claims/authnmethodsreferences" },
    };

    [Theory]
    [MemberData(nameof(ExtendedShortLongPairs))]
    public async Task GetCurrentActorAsync_ActorIdClaim_ExtendedShortFormConfigured_FallsBackToLongForm(string shortForm, string longForm)
    {
        // Forward direction: consumer configures the short form, identity (post-
        // MapInboundClaims = true) carries the long form. Without the fallback,
        // identity.FindFirst(shortForm) returns null and the request 401s.
        var user = AuthenticatedUser(new Claim(longForm, $"value-{shortForm}"));
        var options = new ClaimsActorOptions { ActorIdClaim = shortForm };

        var actor = (await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be($"value-{shortForm}",
            $"configured '{shortForm}' must fall back to long-form '{longForm}' when MapInboundClaims = true remaps it");
    }

    [Theory]
    [MemberData(nameof(ExtendedShortLongPairs))]
    public async Task GetCurrentActorAsync_ActorIdClaim_ExtendedLongFormConfigured_FallsBackToShortForm(string shortForm, string longForm)
    {
        // Reverse direction: consumer configures the long form, identity carries the
        // short. Pins that the symmetry holds for every extended entry.
        var user = AuthenticatedUser(new Claim(shortForm, $"value-{shortForm}"));
        var options = new ClaimsActorOptions { ActorIdClaim = longForm };

        var actor = (await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be($"value-{shortForm}",
            $"configured '{longForm}' must fall back to short form '{shortForm}'");
    }

    [Fact]
    public async Task GetCurrentActorAsync_PermissionsClaim_AmrLongFormConfigured_MultipleShortValuesAllContribute()
    {
        // Pins the multi-valued case for the extended set: "amr" is commonly emitted as
        // multiple Claim instances (e.g. ["pwd", "mfa"]). When PermissionsClaim is
        // configured as the long form, every short-variant value must contribute.
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("amr", "pwd"),
            new Claim("amr", "mfa"));
        var options = new ClaimsActorOptions { PermissionsClaim = "http://schemas.microsoft.com/claims/authnmethodsreferences" };

        var actor = (await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Permissions.Should().BeEquivalentTo(["pwd", "mfa"],
            "multi-valued 'amr' claims must all contribute when configured as the long form authnmethodsreferences URN");
    }

    #endregion

    #region Custom claim mapping

    [Fact]
    public async Task GetCurrentActorAsync_CustomActorIdClaim_UsesConfiguredClaim()
    {
        var user = AuthenticatedUser(new Claim("oid", "user-oid-456"));
        var options = new ClaimsActorOptions { ActorIdClaim = "oid" };

        var actor = (await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("user-oid-456");
    }

    [Fact]
    public async Task GetCurrentActorAsync_CustomPermissionsClaim_UsesConfiguredClaim()
    {
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("roles", "Admin"),
            new Claim("roles", "Editor"));
        var options = new ClaimsActorOptions { PermissionsClaim = "roles" };

        var actor = (await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Permissions.Should().BeEquivalentTo(["Admin", "Editor"]);
    }

    #endregion

    #region Error handling

    [Fact]
    public async Task GetCurrentActorAsync_NoHttpContext_ThrowsInvalidOperationException()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var provider = new ClaimsActorProvider(accessor, Options.Create(new ClaimsActorOptions()));

        Func<Task> act = async () => await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*HttpContext*");
    }

    [Fact]
    public async Task GetCurrentActorAsync_NotAuthenticated_ReturnsNone()
    {
        // No authenticated identity is client-error state, not infrastructure failure.
        // The provider returns Maybe<Actor>.None and the mediator pipeline emits HTTP 401.
        var user = new ClaimsPrincipal(new ClaimsIdentity()); // no auth type
        var provider = CreateProvider(user);

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public async Task GetCurrentActorAsync_MissingActorIdClaim_ReturnsNone()
    {
        // Authenticated identity is present but the configured ActorIdClaim is missing.
        // From the framework's POV the actor is unidentifiable — same client-facing outcome
        // as no identity. Provider returns Maybe<Actor>.None; mediator emits HTTP 401.
        var user = AuthenticatedUser(new Claim("oid", "user-1")); // has oid but not sub

        var result = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasNoValue.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetCurrentActorAsync_EmptyOrWhitespaceActorIdClaim_ReturnsNone(string claimValue)
    {
        // The configured ActorIdClaim is present but carries an empty / whitespace-only value
        // (malformed token). The provider must return Maybe.None rather than letting the empty
        // string flow into Actor.Create, which would otherwise throw ArgumentException.
        var user = AuthenticatedUser(new Claim("sub", claimValue));

        var result = await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasNoValue.Should().BeTrue();
    }

    #endregion

    #region Nested-claim mapping (ga-15)

    [Fact]
    public async Task GetCurrentActorAsync_NestedJsonClaim_NotAutoFlattened()
    {
        // ga-15: ClaimsActorProvider does flat ClaimType matching only — a JSON-object
        // claim is stored as the raw serialized string, not unwrapped. Consumers must
        // subclass to project nested values into Actor.Permissions.
        var nested = """{"role":"admin","tier":"gold"}""";
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("app_metadata", nested));
        var options = new ClaimsActorOptions { PermissionsClaim = "app_metadata" };

        var actor = (await CreateProvider(user, options).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Permissions.Should().BeEquivalentTo([nested],
            "the default provider returns the raw claim value verbatim and does not parse JSON");
    }

    [Fact]
    public async Task GetCurrentActorAsync_Subclass_CanProjectNestedJsonClaim()
    {
        // ga-15: documented subclassing pattern for nested-claim mapping.
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("app_metadata", """{"roles":["orders:read","orders:write"]}"""));

        var httpContext = new DefaultHttpContext { User = user };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var provider = new NestedJsonRolesActorProvider(accessor, Options.Create(new ClaimsActorOptions()));

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Id.Value.Should().Be("user-1");
        actor.Permissions.Should().BeEquivalentTo(["orders:read", "orders:write"]);
    }

    private sealed class NestedJsonRolesActorProvider(
        IHttpContextAccessor httpContextAccessor,
        IOptions<ClaimsActorOptions> options) : ClaimsActorProvider(httpContextAccessor, options)
    {
        public override Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var httpContext = HttpContextAccessor.HttpContext!;
            var identity = (ClaimsIdentity)httpContext.User.Identities.First(i => i.IsAuthenticated);

            var id = identity.FindFirst(Options.ActorIdClaim)!.Value;
            var nested = identity.FindFirst("app_metadata")?.Value;
            var permissions = nested is null
                ? new HashSet<string>()
                : System.Text.Json.JsonDocument.Parse(nested)
                    .RootElement.GetProperty("roles")
                    .EnumerateArray()
                    .Select(e => e.GetString()!)
                    .ToHashSet();

            return Task.FromResult(Maybe.From(Actor.Create(id, permissions)));
        }
    }

    #endregion

    #region Multi-identity claim spoofing (S1)

    [Fact]
    public async Task GetCurrentActorAsync_MultipleIdentities_OnlyReadsFromAuthenticatedIdentity()
    {
        // Arrange: principal with authenticated identity + unauthenticated identity with spoofed claims
        var authenticatedIdentity = new ClaimsIdentity(
        [
            new Claim("sub", "real-user-123"),
            new Claim("permissions", "orders:read")
        ], "Bearer"); // authenticationType = "Bearer" → IsAuthenticated = true

        var spoofedIdentity = new ClaimsIdentity(
        [
            new Claim("sub", "admin-999"),
            new Claim("permissions", "admin"),
            new Claim("permissions", "orders:delete")
        ]); // no authenticationType → IsAuthenticated = false

        var principal = new ClaimsPrincipal(new[] { authenticatedIdentity, spoofedIdentity });
        var provider = CreateProvider(principal);

        // Act
        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        // Assert — actor should ONLY have claims from the authenticated identity
        actor.Id.Value.Should().Be("real-user-123", "should read from authenticated identity, not spoofed");
        actor.HasPermission("orders:read").Should().BeTrue();
        actor.HasPermission("admin").Should().BeFalse("should NOT have permissions from unauthenticated identity");
        actor.HasPermission("orders:delete").Should().BeFalse("should NOT have permissions from unauthenticated identity");
    }

    [Fact]
    public async Task GetCurrentActorAsync_SpoofedIdentityFirst_OnlyReadsFromAuthenticatedIdentity()
    {
        // Arrange: spoofed (unauthenticated) identity listed FIRST — worst case for FindFirstValue
        var spoofedIdentity = new ClaimsIdentity(
        [
            new Claim("sub", "admin-999"),
            new Claim("permissions", "admin"),
            new Claim("permissions", "orders:delete")
        ]); // no authenticationType → IsAuthenticated = false

        var authenticatedIdentity = new ClaimsIdentity(
        [
            new Claim("sub", "real-user-123"),
            new Claim("permissions", "orders:read")
        ], "Bearer");

        // Spoofed identity is first — FindFirstValue("sub") would return "admin-999" before the fix
        var principal = new ClaimsPrincipal(new[] { spoofedIdentity, authenticatedIdentity });
        var provider = CreateProvider(principal);

        // Act
        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        // Assert
        actor.Id.Value.Should().Be("real-user-123", "should ignore unauthenticated identity even when listed first");
        actor.HasPermission("orders:read").Should().BeTrue();
        actor.HasPermission("admin").Should().BeFalse("should NOT have permissions from unauthenticated identity");
        actor.HasPermission("orders:delete").Should().BeFalse("should NOT have permissions from unauthenticated identity");
    }

    #endregion

    #region ForbiddenPermissions and Attributes defaults

    [Fact]
    public async Task GetCurrentActorAsync_DefaultOptions_ForbiddenPermissionsIsEmpty()
    {
        var user = AuthenticatedUser(new Claim("sub", "user-1"));

        var actor = (await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.ForbiddenPermissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCurrentActorAsync_DefaultOptions_AttributesIsEmpty()
    {
        var user = AuthenticatedUser(new Claim("sub", "user-1"));

        var actor = (await CreateProvider(user).GetCurrentActorAsync(TestContext.Current.CancellationToken)).Unwrap();

        actor.Attributes.Should().BeEmpty();
    }

    #endregion

    #region Silent-403 diagnostics (warn-on-empty-permissions + JSON-shape probe)

    [Fact]
    public async Task GetCurrentActorAsync_EmptyPermissionsOnAuthenticatedIdentity_LogsWarningOnce()
    {
        ClaimsActorProvider.ResetDiagnosticThrottlesForTests();
        var entries = new List<(LogLevel Level, EventId EventId, string Message)>();
        var logger = new FakeLogger(entries);

        // Identity carries a sub but no claim that matches the configured PermissionsClaim
        // ("permissions" default). The empty-permissions warning should fire.
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("app_metadata", "{\"roles\":[\"orders:read\"]}"));

        var provider = CreateProvider(user, logger: logger);

        var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Unwrap().Permissions.Should().BeEmpty(
            "the literal 'permissions' claim is not present on this identity");
        entries.Should().ContainSingle(e => e.EventId.Id == 2)
            .Which.Level.Should().Be(LogLevel.Warning);

        // Calling a second time must NOT emit a second warning (throttled).
        await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        entries.Count(e => e.EventId.Id == 2).Should().Be(1,
            "the empty-permissions warning is throttled to once per application lifetime");
    }

    [Fact]
    public async Task GetCurrentActorAsync_PermissionsClaimResolvesToJsonObject_LogsErrorOnce()
    {
        ClaimsActorProvider.ResetDiagnosticThrottlesForTests();
        var entries = new List<(LogLevel Level, EventId EventId, string Message)>();
        var logger = new FakeLogger(entries);

        // Configure PermissionsClaim to a name whose VALUE is a JSON object — the
        // smoking-gun shape that NestedJsonPathClaimsActorProvider was added to address.
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("app_metadata", "{\"roles\":[\"orders:read\"]}"));

        var provider = CreateProvider(
            user,
            options: new ClaimsActorOptions { PermissionsClaim = "app_metadata" },
            logger: logger);

        await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        entries.Should().Contain(e => e.EventId.Id == 3)
            .Which.Level.Should().Be(LogLevel.Error);

        // Second invocation: throttled.
        await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        entries.Count(e => e.EventId.Id == 3).Should().Be(1,
            "the JSON-shape probe is throttled to once per application lifetime");
    }

    [Fact]
    public async Task GetCurrentActorAsync_ValidateClaimShapeOnFirstUse_False_SuppressesDiagnostics()
    {
        ClaimsActorProvider.ResetDiagnosticThrottlesForTests();
        var entries = new List<(LogLevel Level, EventId EventId, string Message)>();
        var logger = new FakeLogger(entries);

        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("app_metadata", "{\"roles\":[\"orders:read\"]}"));

        var provider = CreateProvider(
            user,
            options: new ClaimsActorOptions
            {
                PermissionsClaim = "app_metadata",
                ValidateClaimShapeOnFirstUse = false,
            },
            logger: logger);

        await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        entries.Where(e => e.EventId.Id is 2 or 3).Should().BeEmpty(
            "ValidateClaimShapeOnFirstUse=false must suppress both diagnostics");
    }

    [Fact]
    public async Task GetCurrentActorAsync_PermissionsResolvedFlat_DoesNotEmitDiagnostics()
    {
        ClaimsActorProvider.ResetDiagnosticThrottlesForTests();
        var entries = new List<(LogLevel Level, EventId EventId, string Message)>();
        var logger = new FakeLogger(entries);

        // Configured PermissionsClaim resolves cleanly to scalar string values — neither
        // diagnostic should fire.
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("permissions", "orders:read"),
            new Claim("permissions", "orders:write"));

        var provider = CreateProvider(user, logger: logger);

        var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Unwrap().Permissions.Should().BeEquivalentTo(["orders:read", "orders:write"]);
        entries.Where(e => e.EventId.Id is 2 or 3).Should().BeEmpty();
    }

    [Fact]
    public async Task GetCurrentActorAsync_SinglePermissionResolvedToJsonValue_LogsJsonShapeError()
    {
        ClaimsActorProvider.ResetDiagnosticThrottlesForTests();
        var entries = new List<(LogLevel Level, EventId EventId, string Message)>();
        var logger = new FakeLogger(entries);

        // The configured PermissionsClaim resolves to exactly ONE value, and that value is
        // a JSON document. This is the post-fallback signal that the consumer probably
        // wanted nested-JSON traversal — the diagnostic must fire even when the resolved
        // permissions set is non-empty (count == 1).
        var user = AuthenticatedUser(
            new Claim("sub", "user-1"),
            new Claim("permissions", """{"roles":["orders:read"]}"""));

        var provider = CreateProvider(user, logger: logger);

        await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        entries.Should().Contain(e => e.EventId.Id == 3)
            .Which.Level.Should().Be(LogLevel.Error,
            "JSON-shape probe must fire when the single resolved permission value is JSON-shaped");
    }

    #endregion

    #region Silent-401 diagnostics (unresolvable actor id)

    [Fact]
    public async Task GetCurrentActorAsync_UnresolvableActorIdOnAuthenticatedIdentity_LogsWarningOnce()
    {
        ClaimsActorProvider.ResetDiagnosticThrottlesForTests();
        var entries = new List<(LogLevel Level, EventId EventId, string Message)>();
        var logger = new FakeLogger(entries);

        // Authenticated identity that carries claims, but none the configured ActorIdClaim
        // ("sub" default) or its known-name counterpart can resolve. The request 401s; without
        // a diagnostic nothing points at the claim mapping as the cause.
        var user = AuthenticatedUser(
            new Claim("user_id", "user-1"),
            new Claim("email", "a@b.test"));

        var provider = CreateProvider(user, logger: logger);

        var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.HasValue.Should().BeFalse("the configured ActorIdClaim resolved nothing");
        var entry = entries.Should().ContainSingle(e => e.EventId.Id == 5).Which;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("sub", "the diagnostic must name the configured claim");
        entry.Message.Should().Contain("user_id", "the diagnostic must list the claims actually present");

        // Throttled: a second unresolvable request must not emit a second warning.
        await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);
        entries.Count(e => e.EventId.Id == 5).Should().Be(1);
    }

    [Fact]
    public async Task GetCurrentActorAsync_UnresolvableActorId_SuppressesDiagnosticWhenValidationDisabled()
    {
        ClaimsActorProvider.ResetDiagnosticThrottlesForTests();
        var entries = new List<(LogLevel Level, EventId EventId, string Message)>();
        var logger = new FakeLogger(entries);
        var options = new ClaimsActorOptions { ValidateClaimShapeOnFirstUse = false };

        var user = AuthenticatedUser(new Claim("user_id", "user-1"));

        var provider = CreateProvider(user, options, logger);

        (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).HasValue.Should().BeFalse();
        entries.Should().NotContain(e => e.EventId.Id == 5);
    }

    [Fact]
    public async Task GetCurrentActorAsync_NoAuthenticatedIdentity_DoesNotLogUnresolvableActorId()
    {
        ClaimsActorProvider.ResetDiagnosticThrottlesForTests();
        var entries = new List<(LogLevel Level, EventId EventId, string Message)>();
        var logger = new FakeLogger(entries);

        // An anonymous request is ordinary 401 traffic, not a claim-mapping misconfiguration.
        // Diagnosing it would fire on every unauthenticated caller and bury the real signal.
        var provider = CreateProvider(new ClaimsPrincipal(new ClaimsIdentity()), logger: logger);

        (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).HasValue.Should().BeFalse();
        entries.Should().NotContain(e => e.EventId.Id == 5);
    }

    [Fact]
    public async Task GetCurrentActorAsync_ActorIdResolvedViaKnownNameFallback_DoesNotLogUnresolvableActorId()
    {
        ClaimsActorProvider.ResetDiagnosticThrottlesForTests();
        var entries = new List<(LogLevel Level, EventId EventId, string Message)>();
        var logger = new FakeLogger(entries);

        // Configured "sub" but the token carries the MapInboundClaims-remapped long form.
        // The fallback resolves it, so this is not a misconfiguration and must stay quiet.
        var user = AuthenticatedUser(new Claim(ClaimTypes.NameIdentifier, "user-1"));

        var provider = CreateProvider(user, logger: logger);

        (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).HasValue.Should().BeTrue();
        entries.Should().NotContain(e => e.EventId.Id == 5);
    }

    #endregion
}