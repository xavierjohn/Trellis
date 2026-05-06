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
}
