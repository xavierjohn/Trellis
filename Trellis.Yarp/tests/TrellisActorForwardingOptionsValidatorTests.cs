namespace Trellis.Yarp.Tests;

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using global::Microsoft.IdentityModel.Tokens;

/// <summary>
/// Tests for <see cref="TrellisActorForwardingOptionsValidator"/>. The validator runs at
/// host start via <c>services.AddOptions&lt;TrellisActorForwardingOptions&gt;()
/// .ValidateOnStart()</c>; these tests exercise it in isolation.
/// </summary>
public sealed class TrellisActorForwardingOptionsValidatorTests
{
    private static readonly TrellisActorForwardingOptionsValidator Validator = new();

    [Fact]
    public void Validate_ValidOptions_Passes()
    {
        var options = Valid();

        var result = Validator.Validate(name: null, options);

        result.Succeeded.Should().BeTrue(BecauseOf(result));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyIssuer_Fails(string? issuer)
    {
        var options = Valid(b => b.Issuer = issuer!);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(TrellisActorForwardingOptions.Issuer));
    }

    [Fact]
    public void Validate_NullPublicBaseUrl_Fails()
    {
        var options = Valid(b => b.PublicBaseUrl = null!);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(TrellisActorForwardingOptions.PublicBaseUrl));
        result.FailureMessage.Should().Contain("required");
    }

    [Fact]
    public void Validate_RelativePublicBaseUrl_Fails()
    {
        var options = Valid(b => b.PublicBaseUrl = new Uri("/relative", UriKind.Relative));

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("absolute");
        result.FailureMessage.Should().Contain("spoofable");
    }

    [Fact]
    public void Validate_NullSigningCredentials_Fails()
    {
        var options = Valid(b => b.SigningCredentials = null!);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(TrellisActorForwardingOptions.SigningCredentials));
        result.FailureMessage.Should().Contain("asymmetric");
    }

    [Fact]
    public void Validate_SymmetricSigningKey_Fails()
    {
        // 64 bytes of zero is fine for the test — we only care that the validator rejects
        // it as a symmetric key before any signing actually happens.
        var symmetric = new SymmetricSecurityKey(new byte[64]) { KeyId = "sym-1" };
        var credentials = new SigningCredentials(symmetric, SecurityAlgorithms.HmacSha256);
        var options = Valid(b => b.SigningCredentials = credentials);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("symmetric");
        result.FailureMessage.Should().Contain("JWKS");
    }

    [Fact]
    public void Validate_JsonWebKeyWithOctKty_RejectedAsSymmetric()
    {
        // Round-1 security review fix: JsonWebKey { Kty: "oct" } is also symmetric.
        // Without this guard, a consumer could bypass the asymmetric-only contract by
        // passing new SigningCredentials(jsonWebKeyWithOctKty, HmacSha256) through validation.
        var jwk = new JsonWebKey
        {
            Kty = JsonWebAlgorithmsKeyTypes.Octet,
            K = Convert.ToBase64String(new byte[32]),
            KeyId = "oct-1",
        };
        var credentials = new SigningCredentials(jwk, SecurityAlgorithms.HmacSha256);
        var options = Valid(b => b.SigningCredentials = credentials);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("symmetric");
        result.FailureMessage.Should().Contain("kty=\"oct\"");
    }

    [Theory]
    [InlineData(SecurityAlgorithms.HmacSha256)]
    [InlineData(SecurityAlgorithms.HmacSha384)]
    [InlineData(SecurityAlgorithms.HmacSha512)]
    public void Validate_HmacAlgorithm_Rejected(string algorithm)
    {
        // Defense-in-depth against a future SecurityKey subclass that confuses the
        // structural IsSymmetric check: also reject HMAC algorithms at the SigningCredentials
        // level. RsaSha256 key + HmacSha256 algorithm would be nonsense and should fail.
        var rsa = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "rsa-1" };
        var credentials = new SigningCredentials(rsa, algorithm);
        var options = Valid(b => b.SigningCredentials = credentials);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("HMAC");
        result.FailureMessage.Should().Contain("symmetric");
    }

    [Fact]
    public void Validate_X509SecurityKey_RejectedAsUnsupported()
    {
        // Round-1 security review fix: X509SecurityKey passes the asymmetric structural check
        // but the JWKS builder does not yet emit x5c / x5t for X509, so the published JWKS
        // would be unusable for downstream signature validation. Reject at startup with
        // explicit guidance to unwrap to RsaSecurityKey / ECDsaSecurityKey before assigning.
        using var rsa = RSA.Create(2048);
        var cert = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                "CN=test", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1));
        var x509Key = new X509SecurityKey(cert) { KeyId = "x509-1" };
        var credentials = new SigningCredentials(x509Key, SecurityAlgorithms.RsaSha256);
        var options = Valid(b => b.SigningCredentials = credentials);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("X509SecurityKey");
        result.FailureMessage.Should().Contain("Unwrap");
    }

    [Fact]
    public void Validate_SigningKeyMissingKid_Fails()
    {
        var rsa = RSA.Create(2048);
        var rsaKey = new RsaSecurityKey(rsa);                    // no KeyId
        var credentials = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256);
        var options = Valid(b => b.SigningCredentials = credentials);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("kid");
        result.FailureMessage.Should().Contain("rotation");
    }

    [Fact]
    public void Validate_PreviousSigningKeyMissingKid_Fails()
    {
        var previous = new RsaSecurityKey(RSA.Create(2048));     // no KeyId
        var options = Valid(b => b.PreviousSigningKeys = [previous]);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("PreviousSigningKeys");
        result.FailureMessage.Should().Contain("kid");
    }

    [Fact]
    public void Validate_PreviousSigningKeySymmetric_Fails()
    {
        var symmetric = new SymmetricSecurityKey(new byte[64]) { KeyId = "sym-prev" };
        var options = Valid(b => b.PreviousSigningKeys = [symmetric]);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("PreviousSigningKeys");
        result.FailureMessage.Should().Contain("symmetric");
    }

    [Fact]
    public void Validate_PreviousSigningKeyNull_Fails()
    {
        var options = Valid(b => b.PreviousSigningKeys = [null!]);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("PreviousSigningKeys[0] is null");
    }

    [Fact]
    public void Validate_NullPreviousSigningKeysList_Fails()
    {
        var options = Valid(b => b.PreviousSigningKeys = null!);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("PreviousSigningKeys");
        result.FailureMessage.Should().Contain("empty list");
    }

    [Fact]
    public void Validate_RotationRingDuplicateKid_Fails()
    {
        // Active and previous both have kid "active-1" — JWKS lookup would be ambiguous.
        var previous = NewRsaKey(kid: "active-1");
        var options = Valid(b => b.PreviousSigningKeys = [previous]);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("rotation ring");
        result.FailureMessage.Should().Contain("active-1");
    }

    [Fact]
    public void Validate_PreviousKeysWithUniqueKids_Passes()
    {
        var previous1 = NewRsaKey(kid: "prev-1");
        var previous2 = NewRsaKey(kid: "prev-2");
        var options = Valid(b => b.PreviousSigningKeys = [previous1, previous2]);

        var result = Validator.Validate(name: null, options);

        result.Succeeded.Should().BeTrue(BecauseOf(result));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveLifetime_Fails(int seconds)
    {
        var options = Valid(b => b.Lifetime = TimeSpan.FromSeconds(seconds));

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(TrellisActorForwardingOptions.Lifetime));
        result.FailureMessage.Should().Contain("positive");
    }

    [Fact]
    public void Validate_LifetimeBelowMinimum_Fails()
    {
        var options = Valid(b => b.Lifetime = TimeSpan.FromSeconds(30));

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(TrellisActorForwardingOptions.Lifetime));
        result.FailureMessage.Should().Contain("00:01:00");
    }

    [Fact]
    public void Validate_LifetimeAboveMaximum_Fails()
    {
        var options = Valid(b => b.Lifetime = TimeSpan.FromMinutes(31));

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(TrellisActorForwardingOptions.Lifetime));
        result.FailureMessage.Should().Contain("00:30:00");
    }

    [Theory]
    [InlineData(60)]    // 1 minute — lower bound
    [InlineData(300)]   // 5 minutes — default
    [InlineData(1800)]  // 30 minutes — upper bound
    public void Validate_LifetimeAtBoundary_Passes(int seconds)
    {
        var options = Valid(b => b.Lifetime = TimeSpan.FromSeconds(seconds));

        var result = Validator.Validate(name: null, options);

        result.Succeeded.Should().BeTrue(BecauseOf(result));
    }

    [Fact]
    public void Validate_NullAudiencePerCluster_Fails()
    {
        var options = Valid(b => b.AudiencePerCluster = null!);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(TrellisActorForwardingOptions.AudiencePerCluster));
    }

    [Fact]
    public void Validate_NullProjectPermissionsFor_Fails()
    {
        var options = Valid(b => b.ProjectPermissionsFor = null!);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(TrellisActorForwardingOptions.ProjectPermissionsFor));
    }

    [Fact]
    public void Validate_NullProjectForbiddenFor_Fails()
    {
        var options = Valid(b => b.ProjectForbiddenFor = null!);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(TrellisActorForwardingOptions.ProjectForbiddenFor));
        result.FailureMessage.Should().Contain("contract integrity invariant");
    }

    [Fact]
    public void Validate_NullProjectAttributes_Fails()
    {
        var options = Valid(b => b.ProjectAttributes = null!);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(TrellisActorForwardingOptions.ProjectAttributes));
    }

    [Fact]
    public void Validate_NullActorIdResolver_Fails()
    {
        var options = Valid(b => b.ActorIdResolver = null!);

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(TrellisActorForwardingOptions.ActorIdResolver));
        result.FailureMessage.Should().Contain("namespace");   // hint about multi-IdP override
    }

    [Fact]
    public void Validate_NullOptions_Throws()
    {
        var act = () => Validator.Validate(name: null, options: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Validate_AccumulatesMultipleFailures()
    {
        // Empty Issuer + relative PublicBaseUrl + symmetric key + bad Lifetime + null callback
        // — every field-level failure should be reported, not just the first.
        var symmetric = new SymmetricSecurityKey(new byte[64]) { KeyId = "sym-multi" };
        var options = new TrellisActorForwardingOptions
        {
            Issuer = "",
            SigningCredentials = new SigningCredentials(symmetric, SecurityAlgorithms.HmacSha256),
            PublicBaseUrl = new Uri("/rel", UriKind.Relative),
            Lifetime = TimeSpan.FromSeconds(10),
            ActorIdResolver = null!,
        };

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures!.Should().HaveCountGreaterThanOrEqualTo(4,
            "the validator must accumulate failures so consumers see every problem in one error, not have to fix them one at a time");
        result.FailureMessage.Should().Contain(nameof(TrellisActorForwardingOptions.Issuer));
        result.FailureMessage.Should().Contain(nameof(TrellisActorForwardingOptions.PublicBaseUrl));
        result.FailureMessage.Should().Contain("symmetric");
        result.FailureMessage.Should().Contain(nameof(TrellisActorForwardingOptions.Lifetime));
        result.FailureMessage.Should().Contain(nameof(TrellisActorForwardingOptions.ActorIdResolver));
    }

    // === Fixtures ===

    private static TrellisActorForwardingOptions Valid(Action<TrellisActorForwardingOptionsBuilder>? mutate = null)
    {
        var builder = new TrellisActorForwardingOptionsBuilder
        {
            Issuer = "https://gateway.internal",
            SigningCredentials = NewRsaSigningCredentials(kid: "active-1"),
            PublicBaseUrl = new Uri("https://gateway.internal", UriKind.Absolute),
        };
        mutate?.Invoke(builder);
        return builder.Build();
    }

    /// <summary>
    /// Mutable builder for <see cref="TrellisActorForwardingOptions"/> so tests can
    /// freely override fields without the `with` expression (the options type is a
    /// sealed class per the Trellis options convention).
    /// </summary>
    private sealed class TrellisActorForwardingOptionsBuilder
    {
        public string Issuer { get; set; } = "";
        public SigningCredentials SigningCredentials { get; set; } = null!;
        public IReadOnlyList<SecurityKey> PreviousSigningKeys { get; set; } = [];
        public Uri PublicBaseUrl { get; set; } = null!;
        public Func<global::Yarp.ReverseProxy.Configuration.ClusterConfig, string> AudiencePerCluster { get; set; }
            = static cluster => cluster.ClusterId;
        public Func<global::Yarp.ReverseProxy.Configuration.ClusterConfig, IReadOnlySet<string>, IReadOnlySet<string>> ProjectPermissionsFor { get; set; }
            = static (_, perms) => perms;
        public Func<global::Yarp.ReverseProxy.Configuration.ClusterConfig, IReadOnlySet<string>, IReadOnlySet<string>> ProjectForbiddenFor { get; set; }
            = static (_, forbidden) => forbidden;
        public Func<global::Yarp.ReverseProxy.Configuration.ClusterConfig, IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>> ProjectAttributes { get; set; }
            = static (_, attrs) => attrs;
        public Func<Actor, string> ActorIdResolver { get; set; }
            = static actor => actor.Id.Value;
        public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(5);

        public TrellisActorForwardingOptions Build() => new()
        {
            Issuer = Issuer,
            SigningCredentials = SigningCredentials,
            PreviousSigningKeys = PreviousSigningKeys,
            PublicBaseUrl = PublicBaseUrl,
            AudiencePerCluster = AudiencePerCluster,
            ProjectPermissionsFor = ProjectPermissionsFor,
            ProjectForbiddenFor = ProjectForbiddenFor,
            ProjectAttributes = ProjectAttributes,
            ActorIdResolver = ActorIdResolver,
            Lifetime = Lifetime,
        };
    }

    private static SigningCredentials NewRsaSigningCredentials(string kid) =>
        new(NewRsaKey(kid), SecurityAlgorithms.RsaSha256);

    private static RsaSecurityKey NewRsaKey(string kid)
    {
        var rsa = RSA.Create(2048);
        return new RsaSecurityKey(rsa) { KeyId = kid };
    }

    private static string BecauseOf(ValidateOptionsResult result) =>
        $"validator should have succeeded but reported: {result.FailureMessage ?? "(no message)"}";
}
