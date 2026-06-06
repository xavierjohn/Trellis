namespace Trellis.Yarp.Tests;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using global::Microsoft.IdentityModel.JsonWebTokens;
using global::Microsoft.IdentityModel.Tokens;
using global::Yarp.ReverseProxy.Configuration;

/// <summary>
/// Tests for <see cref="TrellisActorJwtMinter"/>. Asserts the v1 internal-JWT contract
/// shape end-to-end: header (alg, kid), structural claims (iss, aud, sub, jti, iat, nbf,
/// exp), sentinel + count claims, multi-valued permissions / forbidden, attribute
/// emission, signature validity, projection callback semantics, and the deny-overrides-allow
/// contract-integrity invariant (forbidden count emitted even when empty).
/// </summary>
public sealed class TrellisActorJwtMinterTests
{
    private const string Issuer = "https://gateway.internal";
    private const string ActiveKid = "active-1";

    [Fact]
    public void MintFor_HappyPath_TokenContainsAllContractClaims()
    {
        var (minter, _, _) = NewMinter();
        var actor = NewActor(
            id: "user-42",
            permissions: ["incidents:read", "incidents:write"],
            forbidden: ["incidents:delete"],
            attributes: new Dictionary<string, string> { ["tid"] = "tenant-7" });

        var token = minter.MintFor(actor, NewCluster("incidents")).CompactJws;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        jwt.Issuer.Should().Be(Issuer);
        jwt.Audiences.Should().Equal(["incidents"]);
        jwt.Subject.Should().Be("user-42");
        ClaimValue(jwt, TrellisInternalJwtClaimNames.ContractVersion).Should().Be("1");
        ClaimValue(jwt, TrellisInternalJwtClaimNames.PermissionsCount).Should().Be("2");
        ClaimValue(jwt, TrellisInternalJwtClaimNames.ForbiddenPermissionsCount).Should().Be("1");
        ClaimValues(jwt, TrellisInternalJwtClaimNames.Permissions).Should().BeEquivalentTo(["incidents:read", "incidents:write"]);
        ClaimValues(jwt, TrellisInternalJwtClaimNames.ForbiddenPermissions).Should().BeEquivalentTo(["incidents:delete"]);
        ClaimValue(jwt, "tid").Should().Be("tenant-7");
    }

    [Fact]
    public void MintFor_KidEmittedInJwtHeader()
    {
        var (minter, _, _) = NewMinter();

        var token = minter.MintFor(NewActor(), NewCluster("incidents")).CompactJws;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        jwt.Kid.Should().Be(ActiveKid,
            "downstream JwtBearerHandler (JWKS discovery) and air-gapped static-key-ring consumers MUST resolve the right signing key by kid during rotation; missing kid breaks both validation paths");
    }

    [Fact]
    public void MintFor_AlgEmittedAsRsaSha256()
    {
        var (minter, _, _) = NewMinter();

        var token = minter.MintFor(NewActor(), NewCluster("incidents")).CompactJws;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        jwt.Alg.Should().Be(SecurityAlgorithms.RsaSha256);
    }

    [Fact]
    public async Task MintFor_SignatureVerifiesAgainstPublicKey()
    {
        var (minter, options, _) = NewMinter();
        var publicKey = options.Value.SigningCredentials.Key;

        var token = minter.MintFor(NewActor(), NewCluster("incidents")).CompactJws;

        var handler = new JsonWebTokenHandler();
        var tvp = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = Issuer,
            ValidateAudience = true, ValidAudience = "incidents",
            ValidateLifetime = false,
            ValidateIssuerSigningKey = true, RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            IssuerSigningKey = publicKey,
        };
        var result = await handler.ValidateTokenAsync(token, tvp);

        result.IsValid.Should().BeTrue($"signature should verify; reported: {result.Exception?.Message}");
    }

    [Fact]
    public void MintFor_EmptyPermissions_EmitsCountAsZeroAndNoPermissionsClaims()
    {
        var (minter, _, _) = NewMinter();
        var actor = NewActor(permissions: []);

        var token = minter.MintFor(actor, NewCluster("incidents")).CompactJws;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        ClaimValue(jwt, TrellisInternalJwtClaimNames.PermissionsCount).Should().Be("0");
        ClaimValues(jwt, TrellisInternalJwtClaimNames.Permissions).Should().BeEmpty();
    }

    [Fact]
    public void MintFor_EmptyForbidden_EmitsCountAsZeroAndNoForbiddenClaims()
    {
        // The deny-overrides-allow contract integrity invariant (security review finding #5, Blocking):
        // empty MUST NOT be indistinguishable from absent. A misbehaving proxy stripping the deny
        // claims must be detectable; that detection lives on the count claim, which therefore
        // MUST be emitted even when zero.
        var (minter, _, _) = NewMinter();
        var actor = NewActor(forbidden: []);

        var token = minter.MintFor(actor, NewCluster("incidents")).CompactJws;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        ClaimValue(jwt, TrellisInternalJwtClaimNames.ForbiddenPermissionsCount).Should().Be("0",
            "forbidden_permissions_count MUST be emitted as '0' for empty deny sets — empty MUST NOT be indistinguishable from absent (deny-overrides-allow contract integrity invariant)");
        ClaimValues(jwt, TrellisInternalJwtClaimNames.ForbiddenPermissions).Should().BeEmpty();
    }

    [Fact]
    public void MintFor_EmptyAttributes_EmitsNoAttributeClaims()
    {
        var (minter, _, _) = NewMinter();
        var actor = NewActor(attributes: new Dictionary<string, string>());

        var token = minter.MintFor(actor, NewCluster("incidents")).CompactJws;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        // No way to enumerate "attribute claims" specifically without the contract knowing the names;
        // assert by counting the non-structural claims.
        var structural = new HashSet<string>(StringComparer.Ordinal)
        {
            "iss", "aud", "iat", "nbf", "exp",
            TrellisInternalJwtClaimNames.Subject,
            TrellisInternalJwtClaimNames.JwtId,
            TrellisInternalJwtClaimNames.ContractVersion,
            TrellisInternalJwtClaimNames.PermissionsCount,
            TrellisInternalJwtClaimNames.ForbiddenPermissionsCount,
        };
        jwt.Claims.Where(c => !structural.Contains(c.Type)).Should().BeEmpty();
    }

    [Fact]
    public void MintFor_PermissionsAreMultiValuedNotCommaJoined()
    {
        // The strict-shape contract enforced by the consumer side rejects comma-joined and
        // JSON-shaped permission values. The minter MUST emit each permission as a separate
        // claim instance (JsonWebTokenHandler serializes to a JSON array, which the consumer
        // decodes back into separate ClaimsIdentity claims of the same type).
        var (minter, _, _) = NewMinter();
        var actor = NewActor(permissions: ["a", "b", "c"]);

        var token = minter.MintFor(actor, NewCluster("incidents")).CompactJws;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        var permissionClaims = ClaimValues(jwt, TrellisInternalJwtClaimNames.Permissions).ToList();
        permissionClaims.Should().HaveCount(3);
        permissionClaims.Should().NotContain(v => v.Contains(','),
            "permissions MUST NOT be comma-joined — the consumer's StrictClaimShape check rejects values containing commas");
        permissionClaims.Should().NotContain(v => v.StartsWith('[') || v.StartsWith('{'),
            "permissions MUST NOT be JSON-stringified — the consumer's StrictClaimShape check rejects values starting with '[' or '{'");
    }

    [Fact]
    public void MintFor_ForbiddenPermissionsAreMultiValuedNotCommaJoined()
    {
        var (minter, _, _) = NewMinter();
        var actor = NewActor(forbidden: ["x", "y"]);

        var token = minter.MintFor(actor, NewCluster("incidents")).CompactJws;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        var forbiddenClaims = ClaimValues(jwt, TrellisInternalJwtClaimNames.ForbiddenPermissions).ToList();
        forbiddenClaims.Should().HaveCount(2);
        forbiddenClaims.Should().NotContain(v => v.Contains(','));
        forbiddenClaims.Should().NotContain(v => v.StartsWith('[') || v.StartsWith('{'));
    }

    [Fact]
    public void MintFor_JtiIsFreshGuidNFormatPerCall()
    {
        var (minter, _, _) = NewMinter();

        var token1 = minter.MintFor(NewActor(), NewCluster("incidents")).CompactJws;
        var token2 = minter.MintFor(NewActor(), NewCluster("incidents")).CompactJws;

        var jti1 = ClaimValue(new JsonWebTokenHandler().ReadJsonWebToken(token1), TrellisInternalJwtClaimNames.JwtId);
        var jti2 = ClaimValue(new JsonWebTokenHandler().ReadJsonWebToken(token2), TrellisInternalJwtClaimNames.JwtId);
        jti1.Should().NotBe(jti2, "every mint must produce a fresh jti for audit correlation");
        jti1.Should().HaveLength(32, "jti uses Guid.ToString(\"N\") format (32 hex chars, no dashes)");
        jti1.All(Uri.IsHexDigit).Should().BeTrue();
    }

    [Fact]
    public void MintFor_IatAndExpReflectTimeProviderAndLifetime()
    {
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));
        var (minter, options, _) = NewMinter(timeProvider: fakeTime, lifetime: TimeSpan.FromMinutes(5));

        var token = minter.MintFor(NewActor(), NewCluster("incidents")).CompactJws;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        var iat = jwt.IssuedAt;
        var exp = jwt.ValidTo;
        var nbf = jwt.ValidFrom;
        iat.Should().Be(fakeTime.GetUtcNow().UtcDateTime);
        nbf.Should().Be(fakeTime.GetUtcNow().UtcDateTime);
        (exp - iat).Should().Be(options.Value.Lifetime);
    }

    [Fact]
    public void MintFor_AudienceComesFromAudiencePerCluster()
    {
        var (minter, _, _) = NewMinter(audiencePerCluster: cluster => $"svc-{cluster.ClusterId}");

        var token = minter.MintFor(NewActor(), NewCluster("incidents")).CompactJws;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        jwt.Audiences.Should().Equal(["svc-incidents"]);
    }

    [Fact]
    public void MintFor_DefaultAudienceIsClusterId()
    {
        var (minter, _, _) = NewMinter();

        var token = minter.MintFor(NewActor(), NewCluster("orders-cluster")).CompactJws;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        jwt.Audiences.Should().Equal(["orders-cluster"]);
    }

    [Fact]
    public void MintFor_SubjectComesFromActorIdResolver()
    {
        var (minter, _, _) = NewMinter(actorIdResolver: a => $"https://idp.example|{a.Id.Value}");
        var actor = NewActor(id: "raw-user-id");

        var token = minter.MintFor(actor, NewCluster("incidents")).CompactJws;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        jwt.Subject.Should().Be("https://idp.example|raw-user-id",
            "ActorIdResolver namespacing is the documented multi-IdP defense against cross-IdP sub collisions");
    }

    [Fact]
    public void MintFor_PermissionsProjectedViaProjectPermissionsFor()
    {
        var (minter, _, _) = NewMinter(projectPermissions: (cluster, perms) =>
            (IReadOnlySet<string>)perms.Where(p => p.StartsWith(cluster.ClusterId + ".", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal));
        var actor = NewActor(permissions: ["incidents.read", "incidents.write", "orders.read"]);

        var token = minter.MintFor(actor, NewCluster("incidents")).CompactJws;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        ClaimValues(jwt, TrellisInternalJwtClaimNames.Permissions).Should().BeEquivalentTo(["incidents.read", "incidents.write"]);
        ClaimValue(jwt, TrellisInternalJwtClaimNames.PermissionsCount).Should().Be("2",
            "count must reflect the PROJECTED set, not the source set");
    }

    [Fact]
    public void MintFor_ForbiddenProjectedViaProjectForbiddenFor()
    {
        var (minter, _, _) = NewMinter(projectForbidden: (cluster, forbidden) =>
            (IReadOnlySet<string>)forbidden.Where(p => p.StartsWith(cluster.ClusterId + ".", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal));
        var actor = NewActor(forbidden: ["incidents.delete", "orders.delete"]);

        var token = minter.MintFor(actor, NewCluster("incidents")).CompactJws;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        ClaimValues(jwt, TrellisInternalJwtClaimNames.ForbiddenPermissions).Should().BeEquivalentTo(["incidents.delete"]);
        ClaimValue(jwt, TrellisInternalJwtClaimNames.ForbiddenPermissionsCount).Should().Be("1");
    }

    [Fact]
    public void MintFor_AttributesProjectedViaProjectAttributes()
    {
        var (minter, _, _) = NewMinter(projectAttributes: (cluster, attrs) =>
            (IReadOnlyDictionary<string, string>)attrs.Where(kv => kv.Key != "internal_only").ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal));
        var actor = NewActor(attributes: new Dictionary<string, string>
        {
            ["tid"] = "tenant-7",
            ["internal_only"] = "secret-do-not-forward",
        });

        var token = minter.MintFor(actor, NewCluster("incidents")).CompactJws;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        ClaimValue(jwt, "tid").Should().Be("tenant-7");
        jwt.Claims.Should().NotContain(c => c.Type == "internal_only");
    }

    [Fact]
    public void MintFor_SameActorDifferentClusters_DifferentAudiencesAndPossiblyDifferentPermissions()
    {
        var (minter, _, _) = NewMinter(projectPermissions: (cluster, perms) =>
            (IReadOnlySet<string>)perms.Where(p => p.StartsWith(cluster.ClusterId + ".", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal));
        var actor = NewActor(permissions: ["incidents.read", "orders.read"]);

        var tokenForIncidents = minter.MintFor(actor, NewCluster("incidents")).CompactJws;
        var tokenForOrders = minter.MintFor(actor, NewCluster("orders")).CompactJws;

        var jwtIncidents = new JsonWebTokenHandler().ReadJsonWebToken(tokenForIncidents);
        var jwtOrders = new JsonWebTokenHandler().ReadJsonWebToken(tokenForOrders);

        jwtIncidents.Audiences.Should().Equal(["incidents"]);
        jwtOrders.Audiences.Should().Equal(["orders"]);
        ClaimValues(jwtIncidents, TrellisInternalJwtClaimNames.Permissions).Should().Equal(["incidents.read"]);
        ClaimValues(jwtOrders, TrellisInternalJwtClaimNames.Permissions).Should().Equal(["orders.read"]);
    }

    [Theory]
    [InlineData(60)]    // 1 minute
    [InlineData(300)]   // 5 minutes (default)
    [InlineData(1800)]  // 30 minutes
    public void MintFor_LifetimeBoundary_ExpEqualsIatPlusLifetime(int lifetimeSeconds)
    {
        var lifetime = TimeSpan.FromSeconds(lifetimeSeconds);
        var (minter, _, _) = NewMinter(lifetime: lifetime);

        var token = minter.MintFor(NewActor(), NewCluster("incidents")).CompactJws;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        (jwt.ValidTo - jwt.IssuedAt).Should().Be(lifetime);
    }

    [Fact]
    public void MintFor_IssuerComesFromOptions()
    {
        var customIssuer = "https://other-gateway.example";
        var (minter, _, _) = NewMinter(issuer: customIssuer);

        var token = minter.MintFor(NewActor(), NewCluster("incidents")).CompactJws;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        jwt.Issuer.Should().Be(customIssuer);
    }

    [Fact]
    public void MintFor_NullActor_Throws()
    {
        var (minter, _, _) = NewMinter();
        var act = () => minter.MintFor(actor: null!, NewCluster("incidents"));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MintFor_NullCluster_Throws()
    {
        var (minter, _, _) = NewMinter();
        var act = () => minter.MintFor(NewActor(), cluster: null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // === PR review feedback (round 3): reserved-claim-name attribute guard ===

    [Theory]
    [InlineData("iss")]
    [InlineData("aud")]
    [InlineData("exp")]
    [InlineData("nbf")]
    [InlineData("iat")]
    [InlineData("jti")]
    [InlineData("sub")]
    [InlineData("ISS")]    // case-insensitive match — JWT claim names are case-sensitive but the structural ones are well-known
    public void MintFor_AttributeWithReservedJwtClaimName_ThrowsLoudly(string reservedName)
    {
        // The minter MUST reject attribute keys that collide with structural JWT claim
        // names (iss/aud/exp/nbf/iat/jti/sub). Emitting a duplicate-name claim would
        // produce a JWT with undefined validation behavior downstream — JwtBearer might
        // read attacker-controlled values for iss/aud/sub. Throwing forces the operator
        // to rename the attribute or filter it in ProjectAttributes.
        var (minter, _, _) = NewMinter(
            projectAttributes: (_, _) => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [reservedName] = "attacker-controlled-value",
            });
        var actor = NewActor(attributes: new Dictionary<string, string> { ["ignored"] = "ignored" });

        var act = () => minter.MintFor(actor, NewCluster("incidents"));

        act.Should().Throw<InvalidOperationException>()
           .WithMessage($"*reserved JWT claim name '{reservedName}'*")
           .WithMessage("*ProjectAttributes*")
           .WithMessage("*external_iss*"); // the suggested workaround
    }

    [Theory]
    [InlineData("permissions")]
    [InlineData("forbidden_permissions")]
    [InlineData("trellis_actor_contract_version")]
    [InlineData("trellis_permissions_count")]
    [InlineData("trellis_forbidden_permissions_count")]
    public void MintFor_AttributeWithTrellisStructuralClaimName_ThrowsLoudly(string structuralName)
    {
        var (minter, _, _) = NewMinter(
            projectAttributes: (_, _) => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [structuralName] = "would-override-structural-claim",
            });

        var act = () => minter.MintFor(NewActor(), NewCluster("incidents"));

        act.Should().Throw<InvalidOperationException>()
           .WithMessage($"*reserved JWT claim name '{structuralName}'*");
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new TrellisActorJwtMinter(options: null!, TimeProvider.System);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullTimeProvider_Throws()
    {
        var options = Options.Create(NewValidOptions());
        var act = () => new TrellisActorJwtMinter(options, timeProvider: null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // === Fixtures ===

    private static (TrellisActorJwtMinter Minter, IOptions<TrellisActorForwardingOptions> Options, FakeTimeProvider TimeProvider) NewMinter(
        FakeTimeProvider? timeProvider = null,
        string? issuer = null,
        TimeSpan? lifetime = null,
        Func<ClusterConfig, string>? audiencePerCluster = null,
        Func<ClusterConfig, IReadOnlySet<string>, IReadOnlySet<string>>? projectPermissions = null,
        Func<ClusterConfig, IReadOnlySet<string>, IReadOnlySet<string>>? projectForbidden = null,
        Func<ClusterConfig, IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? projectAttributes = null,
        Func<Actor, string>? actorIdResolver = null)
    {
        var time = timeProvider ?? new FakeTimeProvider(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));
        var options = Options.Create(NewValidOptions(
            issuer: issuer,
            lifetime: lifetime,
            audiencePerCluster: audiencePerCluster,
            projectPermissions: projectPermissions,
            projectForbidden: projectForbidden,
            projectAttributes: projectAttributes,
            actorIdResolver: actorIdResolver));
        var minter = new TrellisActorJwtMinter(options, time);
        return (minter, options, time);
    }

    private static TrellisActorForwardingOptions NewValidOptions(
        string? issuer = null,
        TimeSpan? lifetime = null,
        Func<ClusterConfig, string>? audiencePerCluster = null,
        Func<ClusterConfig, IReadOnlySet<string>, IReadOnlySet<string>>? projectPermissions = null,
        Func<ClusterConfig, IReadOnlySet<string>, IReadOnlySet<string>>? projectForbidden = null,
        Func<ClusterConfig, IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? projectAttributes = null,
        Func<Actor, string>? actorIdResolver = null)
    {
        var opts = new TrellisActorForwardingOptions
        {
            Issuer = issuer ?? Issuer,
            SigningCredentials = NewRsaSigningCredentials(ActiveKid),
            PublicBaseUrl = new Uri("https://gateway.internal", UriKind.Absolute),
            Lifetime = lifetime ?? TimeSpan.FromMinutes(5),
        };
        if (audiencePerCluster is not null) opts = With(opts, audiencePerCluster: audiencePerCluster);
        if (projectPermissions is not null) opts = With(opts, projectPermissions: projectPermissions);
        if (projectForbidden is not null) opts = With(opts, projectForbidden: projectForbidden);
        if (projectAttributes is not null) opts = With(opts, projectAttributes: projectAttributes);
        if (actorIdResolver is not null) opts = With(opts, actorIdResolver: actorIdResolver);
        return opts;
    }

    private static TrellisActorForwardingOptions With(
        TrellisActorForwardingOptions source,
        Func<ClusterConfig, string>? audiencePerCluster = null,
        Func<ClusterConfig, IReadOnlySet<string>, IReadOnlySet<string>>? projectPermissions = null,
        Func<ClusterConfig, IReadOnlySet<string>, IReadOnlySet<string>>? projectForbidden = null,
        Func<ClusterConfig, IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>>? projectAttributes = null,
        Func<Actor, string>? actorIdResolver = null) => new()
    {
        Issuer = source.Issuer,
        SigningCredentials = source.SigningCredentials,
        PreviousSigningKeys = source.PreviousSigningKeys,
        PublicBaseUrl = source.PublicBaseUrl,
        AudiencePerCluster = audiencePerCluster ?? source.AudiencePerCluster,
        ProjectPermissionsFor = projectPermissions ?? source.ProjectPermissionsFor,
        ProjectForbiddenFor = projectForbidden ?? source.ProjectForbiddenFor,
        ProjectAttributes = projectAttributes ?? source.ProjectAttributes,
        ActorIdResolver = actorIdResolver ?? source.ActorIdResolver,
        Lifetime = source.Lifetime,
    };

    private static SigningCredentials NewRsaSigningCredentials(string kid) =>
        new(new RsaSecurityKey(RSA.Create(2048)) { KeyId = kid }, SecurityAlgorithms.RsaSha256);

    private static Actor NewActor(
        string id = "user-42",
        string[]? permissions = null,
        string[]? forbidden = null,
        Dictionary<string, string>? attributes = null)
        => new(
            id,
            (permissions ?? []).ToHashSet(StringComparer.Ordinal),
            (forbidden ?? []).ToHashSet(StringComparer.Ordinal),
            attributes ?? new Dictionary<string, string>(StringComparer.Ordinal));

    private static ClusterConfig NewCluster(string clusterId) =>
        new() { ClusterId = clusterId };

    private static string ClaimValue(JsonWebToken jwt, string claimType) =>
        jwt.Claims.First(c => c.Type == claimType).Value;

    private static IEnumerable<string> ClaimValues(JsonWebToken jwt, string claimType) =>
        jwt.Claims.Where(c => c.Type == claimType).Select(c => c.Value);
}
