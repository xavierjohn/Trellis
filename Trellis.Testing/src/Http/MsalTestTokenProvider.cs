namespace Trellis.Testing;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Identity.Client;

/// <summary>
/// Acquires real Azure Entra ID tokens for E2E integration tests using
/// the MSAL ROPC (Resource Owner Password Credentials) flow.
/// </summary>
/// <remarks>
/// <para>
/// ROPC is deprecated for production use but remains suitable for automated test
/// scenarios against dedicated test tenants. The test tenant must have ROPC enabled
/// and MFA disabled for test users.
/// </para>
/// <para>
/// Tokens are cached per <see cref="MsalTestTokenProvider"/> instance. Create one
/// instance per test class or fixture to avoid redundant token acquisitions.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var tokenProvider = new MsalTestTokenProvider(msalOptions);
/// var token = await tokenProvider.AcquireTokenAsync("salesRep");
/// client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
/// </code>
/// </example>
[RequiresUnreferencedCode("MSAL uses reflection for token serialization and is not AOT-compatible.")]
public sealed class MsalTestTokenProvider
{
    private readonly MsalTestOptions _options;
    private readonly IPublicClientApplication _app;

    /// <summary>
    /// Initializes a new <see cref="MsalTestTokenProvider"/> with the specified options.
    /// </summary>
    /// <param name="options">MSAL configuration including tenant, client, and test user credentials.</param>
    public MsalTestTokenProvider(MsalTestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;

        _app = PublicClientApplicationBuilder
            .Create(options.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, options.TenantId)
            .Build();
    }

    /// <summary>
    /// Acquires an access token for the named test user using the ROPC flow.
    /// Tokens are cached by MSAL — subsequent calls for the same user return cached tokens
    /// until they expire.
    /// </summary>
    /// <param name="testUserName">
    /// The key in <see cref="MsalTestOptions.TestUsers"/> identifying the test user
    /// (e.g., <c>"salesRep"</c>, <c>"admin"</c>).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The access token string for use in an Authorization Bearer header.</returns>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when <paramref name="testUserName"/> is not found in <see cref="MsalTestOptions.TestUsers"/>.
    /// </exception>
    /// <exception cref="MsalException">
    /// Thrown when token acquisition fails (invalid credentials, tenant misconfiguration, etc.).
    /// </exception>
    public async Task<string> AcquireTokenAsync(
        string testUserName,
        CancellationToken cancellationToken = default)
    {
        if (!_options.TestUsers.TryGetValue(testUserName, out var credentials))
            throw new KeyNotFoundException(
                $"Test user '{testUserName}' not found in MsalTestOptions.TestUsers. " +
                $"Available users: [{string.Join(", ", _options.TestUsers.Keys)}]");

        var scopes = _options.Scopes;

        // ROPC flow — acquire token with username/password (public client, no secret)
        // ROPC is deprecated for production but appropriate for automated test scenarios
        // against dedicated test tenants with MFA disabled.
#pragma warning disable CS0618 // ROPC is intentional for test automation
        var result = await _app
            .AcquireTokenByUsernamePassword(scopes, credentials.Username, credentials.Password)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore CS0618

        return result.AccessToken;
    }
}
