namespace Trellis.Testing;

/// <summary>
/// Credentials for a named test user in an Azure Entra ID test tenant.
/// Used with <see cref="MsalTestOptions"/> to configure E2E integration tests
/// that authenticate with real JWT tokens.
/// </summary>
public sealed class TestUserCredentials
{
    /// <summary>
    /// The user's UPN or email (e.g., <c>"salesrep@contoso.onmicrosoft.com"</c>).
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// The user's password. Store in user-secrets or CI/CD secrets — never in source code.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// The permissions this user is expected to have after authentication.
    /// Useful for test assertions to verify role-to-permission mapping.
    /// </summary>
    public string[] ExpectedPermissions { get; set; } = [];
}