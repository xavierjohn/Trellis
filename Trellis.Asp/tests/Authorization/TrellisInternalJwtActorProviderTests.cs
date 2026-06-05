namespace Trellis.Asp.Tests.Authorization;

using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trellis.Asp.Authorization;
using Trellis.Authorization;

/// <summary>
/// Tests for <see cref="TrellisInternalJwtActorProvider"/>. Mocks <see cref="IAuthenticationService"/>
/// via a fake that returns a configured <see cref="AuthenticateResult"/> for the configured scheme,
/// then exercises the contract-validation paths (sentinel + count claims, strict shape,
/// required attributes, scheme-binding regression, defense-in-depth issuer/audience checks,
/// logging redaction).
/// </summary>
public sealed class TrellisInternalJwtActorProviderTests
{
    private const string Scheme = "Bearer";
    private const string Issuer = "https://gateway.example";
    private const string Audience = "incidents-service";

    // === Happy path ===

    [Fact]
    public async Task GetCurrentActorAsync_HappyPath_ReturnsFullActor()
    {
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .Iss(Issuer)
            .Aud(Audience)
            .ContractVersion("1")
            .Permissions("orders:read", "orders:write")
            .ForbiddenPermissions("orders:archive")
            .Attribute("tid", "tenant-7")
            .Attribute("amr_normalized", "mfa"));
        var (provider, _) = NewProvider(identity, opts => opts
            .WithRequiredAttributes("tenant_id", "mfa")
            .WithAttributeMap("tenant_id", "tid")
            .WithAttributeMap("mfa", "amr_normalized")
            .WithExpectedIssuerAudience(Issuer, Audience));

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Value;

        actor.Id.Value.Should().Be("user-42");
        actor.Permissions.Should().BeEquivalentTo(["orders:read", "orders:write"]);
        actor.ForbiddenPermissions.Should().BeEquivalentTo(["orders:archive"]);
        actor.Attributes.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["tenant_id"] = "tenant-7",
            ["mfa"] = "mfa",
        });
    }

    // === Scheme-binding (amendment 1, security review #13) ===

    [Fact]
    public async Task GetCurrentActorAsync_BearerSchemeDidNotAuthenticate_FailsClosed()
    {
        // HttpContext.User is populated with matching claims but the configured Bearer scheme
        // returned AuthenticateResult.Fail. A misconfigured middleware planting a principal
        // on HttpContext.User MUST NOT silently flow through into a populated Actor.
        var fakeUserClaims = NewIdentity(builder => builder
            .Sub("forged-user")
            .ContractVersion("1")
            .Permissions("orders:read")
            .ForbiddenPermissions());
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(fakeUserClaims) };
        var fakeAuth = new FakeAuthenticationService(AuthenticateResult.Fail("scheme rejected the token"));
        httpContext.RequestServices = BuildRequestServices(fakeAuth);

        var (provider, _) = NewProvider(httpContext, opts => { });

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse(
            "the provider must authenticate the configured scheme explicitly and ignore HttpContext.User");
    }

    [Fact]
    public async Task GetCurrentActorAsync_PrincipalHasNoAuthenticatedIdentity_FailsClosed()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity()); // no AuthenticationType → IsAuthenticated == false
        var (provider, _) = NewProvider(AuthenticateResult.Success(NewTicket(principal)), opts => { });

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse();
    }

    // === Actor-id resolution ===

    [Fact]
    public async Task GetCurrentActorAsync_MissingSubClaim_FailsClosed()
    {
        var identity = NewIdentity(builder => builder
            .ContractVersion("1")
            .Permissions()
            .ForbiddenPermissions());
        var (provider, _) = NewProvider(identity, opts => { });

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task GetCurrentActorAsync_SubRemappedToNameIdentifier_ResolvesViaFallback()
    {
        // JwtBearerOptions.MapInboundClaims = true (the framework default) remaps "sub" to
        // ClaimTypes.NameIdentifier. The fallback machinery must still find the value.
        var identity = NewIdentity(builder => builder
            .Claim(ClaimTypes.NameIdentifier, "user-42")
            .ContractVersion("1")
            .Permissions()
            .ForbiddenPermissions());
        var (provider, _) = NewProvider(identity, opts => { });

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Value;

        actor.Id.Value.Should().Be("user-42");
    }

    // === Sentinel / count claims (amendment 2, security review #5 — deny-strip defense) ===

    [Fact]
    public async Task GetCurrentActorAsync_MissingContractVersionClaim_FailsClosed()
    {
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .Permissions()
            .ForbiddenPermissions()); // no ContractVersion
        var (provider, log) = NewProvider(identity, opts => { });

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse();
        log.Entries.Should().ContainSingle(e => e.EventId.Name == "InternalJwtSentinelMissingOrDuplicated"
            && e.State.Contains("trellis_actor_contract_version"));
    }

    [Fact]
    public async Task GetCurrentActorAsync_WrongContractVersion_FailsClosed()
    {
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("999")
            .Permissions()
            .ForbiddenPermissions());
        var (provider, log) = NewProvider(identity, opts => { });

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse();
        log.Entries.Should().ContainSingle(e => e.EventId.Name == "InternalJwtContractVersionMismatch"
            && !e.State.Contains("999"), "log entry must NOT contain the observed version value (only the expected literal)");
    }

    [Fact]
    public async Task GetCurrentActorAsync_DuplicateContractVersion_FailsClosed()
    {
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .Claim("trellis_actor_contract_version", "1")
            .Claim("trellis_actor_contract_version", "2")
            .Permissions()
            .ForbiddenPermissions());
        var (provider, _) = NewProvider(identity, opts => { });

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task GetCurrentActorAsync_PermissionsCountMismatch_FailsClosed()
    {
        // Count claims 3 but only 2 permissions present — could be a proxy that stripped one.
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Claim("trellis_permissions_count", "3")
            .Claim("permissions", "a")
            .Claim("permissions", "b")
            .ForbiddenPermissions());
        var (provider, log) = NewProvider(identity, opts => { });

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse();
        log.Entries.Should().ContainSingle(e => e.EventId.Name == "InternalJwtPermissionsCountMismatch");
    }

    [Fact]
    public async Task GetCurrentActorAsync_ForbiddenPermissionsCountMismatch_FailsClosed()
    {
        // THE DENY-STRIP DEFENSE: count says 1 but no forbidden_permissions claim present.
        // A proxy stripping the deny set would otherwise let an attacker promote.
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Permissions()
            .Claim("trellis_forbidden_permissions_count", "1")); // no actual forbidden_permissions claim
        var (provider, log) = NewProvider(identity, opts => { });

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse();
        log.Entries.Should().ContainSingle(e => e.EventId.Name == "InternalJwtForbiddenPermissionsCountMismatch");
    }

    [Fact]
    public async Task GetCurrentActorAsync_EmptyForbiddenSetExplicitlyMintedAsZero_AcceptedHappyPath()
    {
        // Contract: empty MUST be emitted as "0", not absent. This test confirms "0" works.
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Permissions("orders:read")
            .Claim("trellis_forbidden_permissions_count", "0")); // explicit empty
        var (provider, _) = NewProvider(identity, opts => { });

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Value;

        actor.ForbiddenPermissions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("0x1")]
    [InlineData("1e3")]
    [InlineData("not-a-number")]
    public async Task GetCurrentActorAsync_MalformedCountClaim_FailsClosed(string malformed)
    {
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Claim("trellis_permissions_count", malformed)
            .ForbiddenPermissions());
        var (provider, log) = NewProvider(identity, opts => { });

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse();
        log.Entries.Should().Contain(e => e.EventId.Name == "InternalJwtCountClaimMalformed");
    }

    // === Multi-valued claims (originally Trellis claim-shape parity) ===

    [Fact]
    public async Task GetCurrentActorAsync_MultiValuedPermissions_RoundTripsAllValues()
    {
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Permissions("a", "b", "c")
            .ForbiddenPermissions());
        var (provider, _) = NewProvider(identity, opts => { });

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Value;

        actor.Permissions.Should().BeEquivalentTo(["a", "b", "c"]);
    }

    [Fact]
    public async Task GetCurrentActorAsync_MultiValuedForbiddenPermissions_RoundTripsAllValues()
    {
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Permissions()
            .ForbiddenPermissions("x", "y"));
        var (provider, _) = NewProvider(identity, opts => { });

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Value;

        actor.ForbiddenPermissions.Should().BeEquivalentTo(["x", "y"]);
    }

    // === StrictClaimShape (amendment 4) ===

    [Theory]
    [InlineData("read,write")]
    [InlineData("read, write")]
    public async Task GetCurrentActorAsync_CommaJoinedPermission_FailsClosedUnderStrictClaimShape(string commaJoined)
    {
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Claim("trellis_permissions_count", "1")
            .Claim("permissions", commaJoined)
            .ForbiddenPermissions());
        var (provider, log) = NewProvider(identity, opts => { });

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse();
        log.Entries.Should().ContainSingle(e => e.EventId.Name == "InternalJwtStrictClaimShapeRejection"
            && e.State.Contains("comma-joined")
            && !e.State.Contains(commaJoined), "log entry must NOT contain the rejected value");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"a\",\"b\"]")]
    [InlineData("{}")]
    [InlineData("{\"foo\":\"bar\"}")]
    public async Task GetCurrentActorAsync_JsonShapedPermission_FailsClosedUnderStrictClaimShape(string jsonShaped)
    {
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Claim("trellis_permissions_count", "1")
            .Claim("permissions", jsonShaped)
            .ForbiddenPermissions());
        var (provider, log) = NewProvider(identity, opts => { });

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse();
        log.Entries.Should().Contain(e => e.EventId.Name == "InternalJwtStrictClaimShapeRejection"
            && e.State.Contains("json-shaped"));
    }

    [Fact]
    public async Task GetCurrentActorAsync_CommaJoinedAttribute_FailsClosedUnderStrictClaimShape()
    {
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Permissions()
            .ForbiddenPermissions()
            .Attribute("tid", "tenant-7,tenant-8"));
        var (provider, _) = NewProvider(identity, opts => opts
            .WithRequiredAttributes("tenant_id")
            .WithAttributeMap("tenant_id", "tid"));

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task GetCurrentActorAsync_CommaJoinedAcceptedUnderRelaxedShape()
    {
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Claim("trellis_permissions_count", "1")
            .Claim("permissions", "read,write")
            .ForbiddenPermissions());
        var (provider, _) = NewProvider(identity, opts => opts.WithStrictClaimShape(false));

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Value;

        actor.Permissions.Should().BeEquivalentTo(["read,write"]); // single bogus permission preserved
    }

    // === RequiredAttributes (amendment 3) ===

    [Fact]
    public async Task GetCurrentActorAsync_RequiredAttributeMissing_FailsClosed()
    {
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Permissions()
            .ForbiddenPermissions()); // no tid claim
        var (provider, log) = NewProvider(identity, opts => opts
            .WithRequiredAttributes("tenant_id")
            .WithAttributeMap("tenant_id", "tid"));

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse();
        log.Entries.Should().ContainSingle(e => e.EventId.Name == "InternalJwtRequiredAttributeMissing"
            && e.State.Contains("tenant_id"));
    }

    [Fact]
    public async Task GetCurrentActorAsync_RequiredAttributeEmpty_FailsClosed()
    {
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Permissions()
            .ForbiddenPermissions()
            .Attribute("tid", ""));
        var (provider, _) = NewProvider(identity, opts => opts
            .WithRequiredAttributes("tenant_id")
            .WithAttributeMap("tenant_id", "tid"));

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task GetCurrentActorAsync_AttributeDuplicated_FailsClosed()
    {
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Permissions()
            .ForbiddenPermissions()
            .Attribute("tid", "tenant-7")
            .Attribute("tid", "tenant-8"));
        var (provider, log) = NewProvider(identity, opts => opts
            .WithRequiredAttributes("tenant_id")
            .WithAttributeMap("tenant_id", "tid"));

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse();
        log.Entries.Should().ContainSingle(e => e.EventId.Name == "InternalJwtAttributeDuplicated");
    }

    [Fact]
    public async Task GetCurrentActorAsync_OptionalAttributeAbsent_OmittedFromActorAttributes()
    {
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Permissions()
            .ForbiddenPermissions()
            .Attribute("tid", "tenant-7")); // mfa missing
        var (provider, _) = NewProvider(identity, opts => opts
            .WithAttributeMap("tenant_id", "tid")
            .WithAttributeMap("mfa", "amr_normalized"));

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Value;

        actor.Attributes.Should().ContainKey("tenant_id");
        actor.Attributes.Should().NotContainKey("mfa");
    }

    // === ExpectedIssuer / ExpectedAudience defense-in-depth (amendment 5) ===

    [Fact]
    public async Task GetCurrentActorAsync_ExpectedIssuerMismatch_FailsClosed()
    {
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Iss("https://attacker.example")
            .Permissions()
            .ForbiddenPermissions());
        var (provider, log) = NewProvider(identity, opts => opts.WithExpectedIssuer(Issuer));

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse();
        log.Entries.Should().ContainSingle(e => e.EventId.Name == "InternalJwtExpectedIssuerMismatch"
            && !e.State.Contains("attacker.example"), "log entry must not contain the actual (attacker-controlled) issuer value");
    }

    [Fact]
    public async Task GetCurrentActorAsync_ExpectedAudienceMismatch_FailsClosed()
    {
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Aud("other-service")
            .Permissions()
            .ForbiddenPermissions());
        var (provider, log) = NewProvider(identity, opts => opts.WithExpectedAudience(Audience));

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse();
        log.Entries.Should().ContainSingle(e => e.EventId.Name == "InternalJwtExpectedAudienceMismatch");
    }

    // === Regression: case-sensitive claim-type lookups (Round-1 GPT-5.5 review finding #1) ===

    [Fact]
    public async Task GetCurrentActorAsync_PermissionsClaimUppercaseDoesNotMatchLowercase_FailsClosed()
    {
        // ClaimsIdentity.FindAll/FindFirst are case-INSENSITIVE. The internal-JWT contract
        // requires case-SENSITIVE matching so case-variant options (e.g. "PERMISSIONS") cannot
        // bypass the validator's reserved-name guard and pick up unrelated claims at runtime.
        // Here PermissionsClaim is set to "PERMISSIONS" but the JWT carries "permissions" —
        // the provider MUST NOT match. The count says 2 (so the contract is internally
        // consistent: 0 PERMISSIONS claims observed == 0 declared via PermissionsCountClaim
        // for the case where consumer expected zero), and we assert that no permissions
        // flowed in via accidental case-insensitive matching.
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Claim("trellis_permissions_count", "0")  // consumer expects zero PERMISSIONS
            .Claim("permissions", "a")                 // lowercase — must NOT be picked up
            .Claim("permissions", "b")
            .ForbiddenPermissions());
        var (provider, _) = NewProvider(identity, opts => opts.WithPermissionsClaim("PERMISSIONS"));

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Value;

        actor.Permissions.Should().BeEmpty(
            "ordinal claim-type matching MUST reject case differences — the lowercase 'permissions' claims must not flow into Actor.Permissions when the option is uppercase");
    }

    [Fact]
    public async Task GetCurrentActorAsync_ReservedClaimNameCaseVariantBypass_BlockedByValidator()
    {
        // The validator now rejects case-variant reserved JWT claim names
        // (StringComparer.OrdinalIgnoreCase, Round-2 fix). This test exercises the
        // RUNTIME defense for the consumer who explicitly sets UnsafeAllowRegisteredClaimNames=true
        // to waive the startup guard — the ordinal claim-type lookup must still prevent a
        // case-variant option (PermissionsClaim = "ISS") from picking up the literal "iss"
        // claim. Defense-in-depth: even if the validator is bypassed, runtime fails closed.
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Claim("iss", "https://attacker.example")    // would be a grant if matched
            .Claim("trellis_permissions_count", "0")
            .ForbiddenPermissions());
        var (provider, _) = NewProvider(identity, opts => opts
            .WithPermissionsClaim("ISS")
            .WithUnsafeAllowRegisteredClaimNames(true));

        var actor = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Value;

        // Runtime ordinal matching means the uppercase option does NOT match the lowercase
        // 'iss' claim — Actor.Permissions stays empty, the bypass fails.
        actor.Permissions.Should().BeEmpty();
    }

    // === Regression: case-insensitive AttributeClaimMap still enforces RequiredAttributes (Round-2 finding) ===

    [Fact]
    public async Task GetCurrentActorAsync_CaseInsensitiveAttributeMapStillEnforcesRequiredAttributes()
    {
        // Defense-in-depth: the configuration below (case-insensitive map key "Tenant_Id" +
        // RequiredAttributes ["tenant_id"]) is NOW rejected at startup by the validator
        // (Round-3 fix — RequiredAttributes must ORDINAL-exactly match a map key). This test
        // intentionally bypasses ValidateOnStart by using Options.Create directly to exercise
        // the RUNTIME defense (HashSet built with the map's comparer). Without the runtime
        // fix, the case-variant RequiredAttributes entry would be missed by the ordinal
        // .Contains on the raw list, silently downgrading required→optional and skipping
        // fail-closed behavior when the claim is absent.
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Permissions()
            .ForbiddenPermissions());                    // 'tid' claim absent
        var caseInsensitiveMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Tenant_Id"] = "tid",
        };
        var (provider, log) = NewProvider(identity, opts => opts
            .WithAttributeMapInstance(caseInsensitiveMap)
            .WithRequiredAttributes("tenant_id"));      // case-variant of map key — would fail ValidateOnStart

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse(
            "RequiredAttributes lookup must use the same comparer as AttributeClaimMap — otherwise a case-insensitive map silently downgrades required attributes to optional");
        log.Entries.Should().ContainSingle(e => e.EventId.Name == "InternalJwtRequiredAttributeMissing");
    }

    // === Regression: ExpectedIssuer must not be satisfied by ClaimTypes.Authentication (Round-1 GPT-5.5 finding #2) ===

    [Fact]
    public async Task GetCurrentActorAsync_ExpectedIssuerSatisfiedOnlyByLiteralIssClaim_NotByAuthenticationClaim()
    {
        // The previous fallback `?? identity.FindFirst(ClaimTypes.Authentication)?.Value`
        // would let a principal satisfy ExpectedIssuer by carrying a ws-* Authentication
        // claim with the expected value, even without an actual 'iss' claim. The fix removes
        // that fallback; this test asserts the new fail-closed behavior.
        var identity = NewIdentity(builder => builder
            .Sub("user-42")
            .ContractVersion("1")
            .Claim(System.Security.Claims.ClaimTypes.Authentication, Issuer)  // attempt to satisfy ExpectedIssuer without 'iss'
            .Permissions()
            .ForbiddenPermissions());
        var (provider, _) = NewProvider(identity, opts => opts.WithExpectedIssuer(Issuer));

        var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        result.HasValue.Should().BeFalse(
            "ExpectedIssuer must require the literal 'iss' claim — ws-* ClaimTypes.Authentication is unrelated to JWT issuer and MUST NOT satisfy the check");
    }

    // === Logging redaction (amendment 7) ===

    [Fact]
    public async Task GetCurrentActorAsync_LoggingRedaction_NeverLogsClaimValuesOnFailurePaths()
    {
        // Build a JWT that fails for every reason we have, and assert no PII / claim value
        // appears in any log entry. Iterate over all failure modes that emit logs.
        var failureScenarios = new (string Name, Action<IdentityBuilder> Build, Action<OptionsBuilder>? Configure)[]
        {
            ("missing_contract_version", b => b.Sub("user-secret-id").Permissions().ForbiddenPermissions(), null),
            ("wrong_contract_version", b => b.Sub("user-secret-id").ContractVersion("999").Permissions().ForbiddenPermissions(), null),
            ("count_mismatch", b => b.Sub("user-secret-id").ContractVersion("1").Claim("trellis_permissions_count", "5").Claim("permissions", "p-secret-value").ForbiddenPermissions(), null),
            ("malformed_count", b => b.Sub("user-secret-id").ContractVersion("1").Claim("trellis_permissions_count", "not-a-number").ForbiddenPermissions(), null),
            ("strict_shape_comma", b => b.Sub("user-secret-id").ContractVersion("1").Claim("trellis_permissions_count", "1").Claim("permissions", "value-with,comma").ForbiddenPermissions(), null),
            ("required_attr_missing", b => b.Sub("user-secret-id").ContractVersion("1").Permissions().ForbiddenPermissions(), o => o.WithRequiredAttributes("tenant_id").WithAttributeMap("tenant_id", "tid")),
            ("expected_issuer_mismatch", b => b.Sub("user-secret-id").Iss("https://gateway-secret-host.example").ContractVersion("1").Permissions().ForbiddenPermissions(), o => o.WithExpectedIssuer("https://gateway.example")),
        };

        var sensitiveSubstrings = new[]
        {
            "user-secret-id",
            "p-secret-value",
            "value-with,comma",
            "gateway-secret-host",
        };

        foreach (var (name, build, configure) in failureScenarios)
        {
            var identity = NewIdentity(build);
            var (provider, log) = NewProvider(identity, configure ?? (o => { }));
            var result = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);
            result.HasValue.Should().BeFalse($"scenario {name} should fail");

            foreach (var entry in log.Entries)
            {
                foreach (var sensitive in sensitiveSubstrings)
                {
                    entry.State.Should().NotContain(sensitive,
                        $"scenario {name} log entry must NOT contain sensitive substring '{sensitive}' — found in: {entry.State}");
                    entry.Message.Should().NotContain(sensitive,
                        $"scenario {name} log message must NOT contain sensitive substring '{sensitive}' — found in: {entry.Message}");
                }
            }
        }
    }

    // === HttpContext missing ===

    [Fact]
    public async Task GetCurrentActorAsync_HttpContextNull_ThrowsInvalidOperationException()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var options = new TrellisInternalJwtActorOptions();
        var provider = new TrellisInternalJwtActorProvider(accessor, Options.Create(options));

        var act = async () => await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*HttpContext*");
    }

    // === IProvideActorVaryHeaders ===

    [Fact]
    public void VaryByHeaders_DefaultBearerScheme_ReturnsAuthorization()
    {
        var accessor = new HttpContextAccessor();
        var options = new TrellisInternalJwtActorOptions();
        var provider = new TrellisInternalJwtActorProvider(accessor, Options.Create(options));

        provider.VaryByHeaders.Should().BeEquivalentTo(["Authorization"]);
    }

    [Fact]
    public void VaryByHeaders_CookieScheme_ReturnsCookieWhenOverridden()
    {
        var accessor = new HttpContextAccessor();
        var options = new TrellisInternalJwtActorOptions
        {
            AuthenticationScheme = "Cookies",
            VaryByHeaders = ["Cookie"],
        };
        var provider = new TrellisInternalJwtActorProvider(accessor, Options.Create(options));

        provider.VaryByHeaders.Should().BeEquivalentTo(["Cookie"]);
    }

    // === Fixture helpers ===

    private static (TrellisInternalJwtActorProvider Provider, CapturingLogger Log) NewProvider(
        ClaimsIdentity identity,
        Action<OptionsBuilder> configureOptions)
    {
        var principal = new ClaimsPrincipal(identity);
        return NewProvider(AuthenticateResult.Success(NewTicket(principal)), configureOptions);
    }

    private static (TrellisInternalJwtActorProvider Provider, CapturingLogger Log) NewProvider(
        AuthenticateResult authResult,
        Action<OptionsBuilder> configureOptions)
    {
        var httpContext = new DefaultHttpContext();
        var fakeAuth = new FakeAuthenticationService(authResult);
        httpContext.RequestServices = BuildRequestServices(fakeAuth);

        return NewProvider(httpContext, configureOptions);
    }

    private static (TrellisInternalJwtActorProvider Provider, CapturingLogger Log) NewProvider(
        HttpContext httpContext,
        Action<OptionsBuilder> configureOptions)
    {
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var optsBuilder = new OptionsBuilder();
        configureOptions(optsBuilder);
        var options = Options.Create(optsBuilder.Build());
        var log = new CapturingLogger();
        var provider = new TrellisInternalJwtActorProvider(accessor, options, log);
        return (provider, log);
    }

    private static AuthenticationTicket NewTicket(ClaimsPrincipal principal) =>
        new(principal, Scheme);

    private static ServiceProvider BuildRequestServices(IAuthenticationService authService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(authService);
        return services.BuildServiceProvider();
    }

    private static ClaimsIdentity NewIdentity(Action<IdentityBuilder> build)
    {
        var builder = new IdentityBuilder();
        build(builder);
        return builder.Build();
    }

    private sealed class IdentityBuilder
    {
        private readonly List<Claim> _claims = [];

        public IdentityBuilder Claim(string type, string value)
        {
            _claims.Add(new Claim(type, value));
            return this;
        }

        public IdentityBuilder Sub(string value) => Claim("sub", value);
        public IdentityBuilder Iss(string value) => Claim("iss", value);
        public IdentityBuilder Aud(string value) => Claim("aud", value);
        public IdentityBuilder ContractVersion(string value) => Claim("trellis_actor_contract_version", value);

        public IdentityBuilder Permissions(params string[] values)
        {
            Claim("trellis_permissions_count", values.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var v in values)
                Claim("permissions", v);
            return this;
        }

        public IdentityBuilder ForbiddenPermissions(params string[] values)
        {
            Claim("trellis_forbidden_permissions_count", values.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var v in values)
                Claim("forbidden_permissions", v);
            return this;
        }

        public IdentityBuilder Attribute(string claimType, string value) => Claim(claimType, value);

        public ClaimsIdentity Build() => new(_claims, authenticationType: "TestBearer");
    }

    private sealed class OptionsBuilder
    {
        private readonly TrellisInternalJwtActorOptions _options = new();

        public OptionsBuilder WithRequiredAttributes(params string[] names)
        {
            _options.RequiredAttributes = names;
            return this;
        }

        public OptionsBuilder WithAttributeMap(string attributeName, string claimType)
        {
            _options.AttributeClaimMap[attributeName] = claimType;
            return this;
        }

        public OptionsBuilder WithAttributeMapInstance(Dictionary<string, string> map)
        {
            _options.AttributeClaimMap = map;
            return this;
        }

        public OptionsBuilder WithExpectedIssuer(string issuer)
        {
            _options.ExpectedIssuer = issuer;
            return this;
        }

        public OptionsBuilder WithExpectedAudience(string audience)
        {
            _options.ExpectedAudience = audience;
            return this;
        }

        public OptionsBuilder WithExpectedIssuerAudience(string issuer, string audience)
        {
            _options.ExpectedIssuer = issuer;
            _options.ExpectedAudience = audience;
            return this;
        }

        public OptionsBuilder WithStrictClaimShape(bool value)
        {
            _options.StrictClaimShape = value;
            return this;
        }

        public OptionsBuilder WithPermissionsClaim(string claimType)
        {
            _options.PermissionsClaim = claimType;
            return this;
        }

        public OptionsBuilder WithUnsafeAllowRegisteredClaimNames(bool value)
        {
            _options.UnsafeAllowRegisteredClaimNames = value;
            return this;
        }

        public TrellisInternalJwtActorOptions Build() => _options;
    }

    private sealed class FakeAuthenticationService(AuthenticateResult result) : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(result);

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;
    }

    private sealed class CapturingLogger : ILogger<TrellisInternalJwtActorProvider>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry
            {
                Level = logLevel,
                EventId = eventId,
                State = state?.ToString() ?? "",
                Exception = exception,
                Message = formatter(state, exception),
            });
    }

    private sealed class LogEntry
    {
        public LogLevel Level { get; init; }
        public EventId EventId { get; init; }
        public string State { get; init; } = "";
        public Exception? Exception { get; init; }
        public string Message { get; init; } = "";
    }
}
