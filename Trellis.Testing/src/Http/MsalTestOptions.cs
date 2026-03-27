namespace Trellis.Testing;

/// <summary>
/// Configuration options for acquiring real Azure Entra ID tokens in E2E integration tests
/// using MSAL (Microsoft Authentication Library).
/// </summary>
/// <remarks>
/// <para>
/// Store sensitive values (ClientSecret, user passwords) in environment variables
/// or test secrets — never commit them to source code.
/// </para>
/// <para>
/// Typical setup:
/// <code>
/// var options = new MsalTestOptions
/// {
///     TenantId = Environment.GetEnvironmentVariable("ENTRA_TEST_TENANT_ID")!,
///     ClientId = Environment.GetEnvironmentVariable("ENTRA_TEST_CLIENT_ID")!,
///     Scopes = ["api://my-api/.default"],
///     TestUsers =
///     {
///         ["salesRep"] = new TestUserCredentials
///         {
///             Username = "salesrep@contoso.onmicrosoft.com",
///             Password = Environment.GetEnvironmentVariable("ENTRA_TEST_SALESREP_PASSWORD")!,
///             ExpectedPermissions = ["orders:create", "orders:read"]
///         }
///     }
/// };
/// </code>
/// </para>
/// </remarks>
public sealed class MsalTestOptions
{
    /// <summary>
    /// The Azure Entra ID tenant ID (GUID or domain, e.g., <c>"contoso.onmicrosoft.com"</c>).
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// The application (client) ID of the Azure Entra app registration.
    /// The app registration must have "Allow public client flows" enabled for ROPC.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// The scopes to request when acquiring tokens.
    /// Typically <c>["api://{clientId}/.default"]</c> for daemon/service tokens.
    /// </summary>
    public string[] Scopes { get; set; } = [];

    /// <summary>
    /// Named test users with credentials and expected permissions.
    /// Use descriptive names matching role conventions (e.g., <c>"salesRep"</c>, <c>"warehouseManager"</c>, <c>"admin"</c>).
    /// </summary>
    public Dictionary<string, TestUserCredentials> TestUsers { get; set; } = new();
}