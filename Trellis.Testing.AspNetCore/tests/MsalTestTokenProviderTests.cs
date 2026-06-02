namespace Trellis.Testing.AspNetCore.Tests;

using System;
using System.Threading.Tasks;
using Trellis.Testing.AspNetCore;

/// <summary>
/// Tests for <see cref="MsalTestTokenProvider"/>. The provider is intentionally not
/// exercised against a real Entra tenant in these tests — only the public-surface
/// argument-validation contracts.
/// </summary>
public sealed class MsalTestTokenProviderTests
{
    private const string TenantIdRequiredMessage = "MsalTestOptions.TenantId must be set to a non-empty Azure AD tenant id (GUID or directory name). MSAL cannot acquire tokens without it.";
    private const string ClientIdRequiredMessage = "MsalTestOptions.ClientId must be set to the application/client id (GUID) of the registered AAD application. MSAL cannot acquire tokens without it.";
    private const string ScopesRequiredMessage = "MsalTestOptions.Scopes must contain at least one scope URI.";

    [Fact]
    public void Constructor_DefaultTenantId_MissingRequiredField_ThrowsInvalidOperationException()
    {
        var options = CreateValidOptions();
        options.TenantId = string.Empty;

#pragma warning disable IL2026 // RequiresUnreferencedCode propagation — test exercises options validation, not the MSAL reflection path.
        var act = () => new MsalTestTokenProvider(options);
#pragma warning restore IL2026

        act.Should().Throw<InvalidOperationException>()
            .WithMessage(TenantIdRequiredMessage);
    }

    [Fact]
    public void Constructor_DefaultClientId_MissingRequiredField_ThrowsInvalidOperationException()
    {
        var options = CreateValidOptions();
        options.ClientId = string.Empty;

#pragma warning disable IL2026 // RequiresUnreferencedCode propagation — test exercises options validation, not the MSAL reflection path.
        var act = () => new MsalTestTokenProvider(options);
#pragma warning restore IL2026

        act.Should().Throw<InvalidOperationException>()
            .WithMessage(ClientIdRequiredMessage);
    }

    [Fact]
    public void Constructor_EmptyScopes_MissingRequiredField_ThrowsInvalidOperationException()
    {
        var options = CreateValidOptions();
        options.Scopes = [];

#pragma warning disable IL2026 // RequiresUnreferencedCode propagation — test exercises options validation, not the MSAL reflection path.
        var act = () => new MsalTestTokenProvider(options);
#pragma warning restore IL2026

        act.Should().Throw<InvalidOperationException>()
            .WithMessage(ScopesRequiredMessage);
    }

    [Fact]
    public void Constructor_ValidOptions_AllRequiredFieldsSet_DoesNotThrow()
    {
        var options = CreateValidOptions();

#pragma warning disable IL2026 // RequiresUnreferencedCode propagation — test exercises options validation, not the MSAL reflection path.
        var act = () => new MsalTestTokenProvider(options);
#pragma warning restore IL2026

        act.Should().NotThrow();
    }

    [Fact]
    public async Task AcquireTokenAsync_NullTestUserName_Throws_ArgumentNullException()
    {
        // Inspection finding m-TA-7: previously a null `testUserName` flowed through
        // _options.TestUsers.TryGetValue(null!, ...) which throws
        // ArgumentNullException(paramName: "key"), confusingly NOT matching the
        // public parameter name `testUserName`. Defensive null-check at the
        // public-API entry point surfaces the right name.
        var options = new MsalTestOptions
        {
            TenantId = "fake-tenant",
            ClientId = Guid.NewGuid().ToString(),
            Scopes = ["api://fake/.default"],
        };
#pragma warning disable IL2026 // RequiresUnreferencedCode propagation — test exercises ArgumentNullException, not the MSAL reflection path.
        var provider = new MsalTestTokenProvider(options);

        var act = async () => await provider.AcquireTokenAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("testUserName");
#pragma warning restore IL2026
    }

    private static MsalTestOptions CreateValidOptions() => new()
    {
        TenantId = "fake-tenant",
        ClientId = Guid.NewGuid().ToString(),
        Scopes = ["api://fake/.default"],
        TestUsers =
        {
            ["salesRep"] = new TestUserCredentials
            {
                Username = "salesrep@contoso.onmicrosoft.com",
                Password = "fake-password",
                ExpectedPermissions = ["orders:read"],
            },
        },
    };
}
