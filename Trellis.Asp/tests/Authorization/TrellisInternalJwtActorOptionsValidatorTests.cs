namespace Trellis.Asp.Tests.Authorization;

using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis.Asp.Authorization;

/// <summary>
/// Tests for <see cref="TrellisInternalJwtActorOptionsValidator"/>. The validator runs at
/// host start via <c>services.AddOptions&lt;TrellisInternalJwtActorOptions&gt;()
/// .ValidateOnStart()</c>; these tests exercise it in isolation.
/// </summary>
public sealed class TrellisInternalJwtActorOptionsValidatorTests
{
    private static readonly TrellisInternalJwtActorOptionsValidator Validator = new();

    [Fact]
    public void Validate_DefaultOptions_Passes()
    {
        var options = new TrellisInternalJwtActorOptions();

        var result = Validator.Validate(name: null, options);

        result.Succeeded.Should().BeTrue(BecauseOf(result));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyAuthenticationScheme_Fails(string scheme)
    {
        var options = new TrellisInternalJwtActorOptions { AuthenticationScheme = scheme };

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(nameof(options.AuthenticationScheme));
    }

    [Theory]
    [InlineData(nameof(TrellisInternalJwtActorOptions.ActorIdClaim))]
    [InlineData(nameof(TrellisInternalJwtActorOptions.PermissionsClaim))]
    [InlineData(nameof(TrellisInternalJwtActorOptions.ForbiddenPermissionsClaim))]
    [InlineData(nameof(TrellisInternalJwtActorOptions.ContractVersionClaim))]
    [InlineData(nameof(TrellisInternalJwtActorOptions.PermissionsCountClaim))]
    [InlineData(nameof(TrellisInternalJwtActorOptions.ForbiddenPermissionsCountClaim))]
    [InlineData(nameof(TrellisInternalJwtActorOptions.ExpectedContractVersion))]
    public void Validate_EmptyStructuralClaim_Fails(string property)
    {
        var options = new TrellisInternalJwtActorOptions();
        switch (property)
        {
            case nameof(TrellisInternalJwtActorOptions.ActorIdClaim): options.ActorIdClaim = ""; break;
            case nameof(TrellisInternalJwtActorOptions.PermissionsClaim): options.PermissionsClaim = ""; break;
            case nameof(TrellisInternalJwtActorOptions.ForbiddenPermissionsClaim): options.ForbiddenPermissionsClaim = ""; break;
            case nameof(TrellisInternalJwtActorOptions.ContractVersionClaim): options.ContractVersionClaim = ""; break;
            case nameof(TrellisInternalJwtActorOptions.PermissionsCountClaim): options.PermissionsCountClaim = ""; break;
            case nameof(TrellisInternalJwtActorOptions.ForbiddenPermissionsCountClaim): options.ForbiddenPermissionsCountClaim = ""; break;
            case nameof(TrellisInternalJwtActorOptions.ExpectedContractVersion): options.ExpectedContractVersion = ""; break;
        }

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(property);
    }

    [Fact]
    public void Validate_DuplicateStructuralClaimNames_Fails()
    {
        // PermissionsClaim and ForbiddenPermissionsClaim collide — both would read from the
        // same claim type, making it impossible to distinguish a grant from a deny.
        var options = new TrellisInternalJwtActorOptions
        {
            PermissionsClaim = "perms",
            ForbiddenPermissionsClaim = "perms",
        };

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Duplicate claim name 'perms'");
    }

    [Theory]
    [InlineData("iss")]
    [InlineData("aud")]
    [InlineData("exp")]
    [InlineData("nbf")]
    [InlineData("iat")]
    [InlineData("jti")]
    [InlineData("sub")]
    public void Validate_ReservedJwtClaimNameAsPermissions_Fails(string reserved)
    {
        var options = new TrellisInternalJwtActorOptions
        {
            PermissionsClaim = reserved,
        };

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain($"reserved JWT claim '{reserved}'");
    }

    [Fact]
    public void Validate_ReservedJwtClaimNameAsForbiddenPermissions_Fails()
    {
        var options = new TrellisInternalJwtActorOptions
        {
            ForbiddenPermissionsClaim = "jti",
        };

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("reserved JWT claim 'jti'");
    }

    [Fact]
    public void Validate_ReservedJwtClaimNameWithUnsafeAllow_Passes()
    {
        // Negative test for the escape — once Unsafe is set, the validator no longer rejects
        // reserved-claim mappings. Caller is responsible for documenting the rationale.
        var options = new TrellisInternalJwtActorOptions
        {
            PermissionsClaim = "iss",
            UnsafeAllowRegisteredClaimNames = true,
        };

        var result = Validator.Validate(name: null, options);

        result.Succeeded.Should().BeTrue(BecauseOf(result));
    }

    [Fact]
    public void Validate_AttributeMappedToReservedJwtClaim_Fails()
    {
        var options = new TrellisInternalJwtActorOptions
        {
            AttributeClaimMap = new Dictionary<string, string> { ["tenant"] = "sub" },
        };

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("AttributeClaimMap['tenant']");
        result.FailureMessage.Should().Contain("reserved JWT claim 'sub'");
    }

    [Theory]
    [InlineData("ISS")]
    [InlineData("Iss")]
    [InlineData("AUD")]
    [InlineData("SuB")]
    public void Validate_ReservedJwtClaimNameAsPermissionsCaseVariant_Fails(string reserved)
    {
        // ClaimsIdentity.FindFirst is case-INsensitive: a case-variant configured name
        // would still resolve to the canonical reserved claim at runtime, bypassing the
        // intended guard. The validator must therefore match reserved names case-insensitively.
        var options = new TrellisInternalJwtActorOptions { PermissionsClaim = reserved };

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain($"reserved JWT claim '{reserved}'");
    }

    [Fact]
    public void Validate_ReservedJwtClaimNameAsAttributeMapValueCaseVariant_Fails()
    {
        var options = new TrellisInternalJwtActorOptions
        {
            AttributeClaimMap = new Dictionary<string, string>
            {
                ["tenant_id"] = "JTI", // case-variant of reserved 'jti'
            },
        };

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("reserved JWT claim 'JTI'");
    }

    [Fact]
    public void Validate_RequiredAttributeCaseVariantOfCaseInsensitiveMapKey_Fails()
    {
        // Actor.Attributes is an ordinal-keyed FrozenDictionary. If AttributeClaimMap uses a
        // case-insensitive comparer, the runtime stores under the map key's spelling
        // ("Tenant_Id"). A required attribute spelled "tenant_id" would be enforced but then
        // emitted under "Tenant_Id" — downstream code querying actor.Attributes["tenant_id"]
        // would silently miss. The validator must reject case-variant required entries even
        // when the map's comparer would consider them equivalent.
        var options = new TrellisInternalJwtActorOptions
        {
            AttributeClaimMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tenant_Id"] = "tid",
            },
            RequiredAttributes = ["tenant_id"], // case-variant of map key
        };

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ORDINAL-exactly match");
    }

    [Fact]
    public void Validate_AttributeClaimMapDuplicateValue_Fails()
    {
        var options = new TrellisInternalJwtActorOptions
        {
            AttributeClaimMap = new Dictionary<string, string>
            {
                ["tenant_id"] = "tid",
                ["org_id"] = "tid",
            },
        };

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("duplicate claim type 'tid'");
    }

    [Fact]
    public void Validate_AttributeClaimMapCollidesWithStructuralClaim_Fails()
    {
        // An attribute mapped to the same claim type as PermissionsClaim is ambiguous —
        // would the provider read it as a permission or as an attribute value?
        var options = new TrellisInternalJwtActorOptions
        {
            PermissionsClaim = "perms",
            AttributeClaimMap = new Dictionary<string, string> { ["tenant_id"] = "perms" },
        };

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("AttributeClaimMap['tenant_id']");
        result.FailureMessage.Should().Contain("structural claim");
    }

    [Fact]
    public void Validate_AttributeClaimMapEmptyClaimType_Fails()
    {
        var options = new TrellisInternalJwtActorOptions
        {
            AttributeClaimMap = new Dictionary<string, string> { ["tenant_id"] = "" },
        };

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("AttributeClaimMap['tenant_id'] is mapped to a null or empty claim type");
    }

    [Fact]
    public void Validate_RequiredAttributeNotInClaimMap_Fails()
    {
        var options = new TrellisInternalJwtActorOptions
        {
            RequiredAttributes = ["tenant_id"],
            AttributeClaimMap = new Dictionary<string, string>
            {
                ["mfa"] = "amr_normalized",
            },
        };

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("RequiredAttributes contains 'tenant_id'");
        result.FailureMessage.Should().Contain("AttributeClaimMap does not map it");
    }

    [Fact]
    public void Validate_RequiredAttributeDuplicate_Fails()
    {
        var options = new TrellisInternalJwtActorOptions
        {
            RequiredAttributes = ["tenant_id", "tenant_id"],
            AttributeClaimMap = new Dictionary<string, string> { ["tenant_id"] = "tid" },
        };

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("duplicate entry 'tenant_id'");
    }

    [Fact]
    public void Validate_RequiredAttributeEmpty_Fails()
    {
        var options = new TrellisInternalJwtActorOptions
        {
            RequiredAttributes = ["  "],
            AttributeClaimMap = new Dictionary<string, string> { ["tenant_id"] = "tid" },
        };

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("RequiredAttributes");
        result.FailureMessage.Should().Contain("null or empty entry");
    }

    [Fact]
    public void Validate_EmptyVaryByHeaders_Fails()
    {
        var options = new TrellisInternalJwtActorOptions { VaryByHeaders = [] };

        var result = Validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("VaryByHeaders must contain at least one header name");
    }

    [Fact]
    public void Validate_VaryByHeadersOverride_Passes()
    {
        // Document the supported scenario: consumer using a non-Bearer scheme overrides Vary.
        var options = new TrellisInternalJwtActorOptions
        {
            AuthenticationScheme = "Cookies",
            VaryByHeaders = ["Cookie"],
        };

        var result = Validator.Validate(name: null, options);

        result.Succeeded.Should().BeTrue(BecauseOf(result));
    }

    [Fact]
    public void Validate_ValidateOnStart_PropagatesValidationFailureFromHost()
    {
        // Integration shape: when registered via AddOptions<>().ValidateOnStart(), invalid
        // options surface as OptionsValidationException on IOptions<T>.Value resolution.
        var services = new ServiceCollection();
        services.AddOptions<TrellisInternalJwtActorOptions>()
            .Configure(o => o.PermissionsClaim = "iss") // reserved → invalid
            .Services
            .AddSingleton<IValidateOptions<TrellisInternalJwtActorOptions>, TrellisInternalJwtActorOptionsValidator>();

        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IOptions<TrellisInternalJwtActorOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*reserved JWT claim 'iss'*");
    }

    private static string BecauseOf(ValidateOptionsResult result) =>
        $"expected Validate to succeed but it failed: {result.FailureMessage}";
}
