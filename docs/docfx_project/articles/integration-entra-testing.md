# Testing with Azure Entra ID Tokens

This guide explains how to set up an Azure Entra ID test tenant for E2E integration testing with real JWT tokens, using the `MsalTestTokenProvider` from `Trellis.Testing`.

## Why Real Tokens?

During development, `DevelopmentActorProvider` reads the `X-Test-Actor` header — fast and convenient for local testing. But for E2E tests that validate the full authentication pipeline (token validation, claim mapping, `EntraActorProvider`), you need real JWT tokens from Azure Entra ID.

## Architecture

```
Development / Unit Tests        E2E Tests (Real Tokens)
┌─────────────────────┐         ┌──────────────────────────┐
│ X-Test-Actor header  │         │ MSAL ROPC flow               │
│ CreateClientWithActor│         │ CreateClientWithEntraTokenAsync│
│ DevelopmentActorProv.│         │ EntraActorProvider            │
└─────────────────────┘         └──────────────────────────┘
```

## Step 1: Create a Test Tenant

1. Go to [Azure Portal](https://portal.azure.com) → **Azure Entra ID** → **Manage tenants** → **Create**
2. Select **Workforce** tenant type
3. Name it (e.g., `contoso-test.onmicrosoft.com`)
4. This tenant is exclusively for automated testing — never reuse a production tenant

## Step 2: Register an App

1. In the test tenant → **App registrations** → **New registration**
2. Name: `{YourService}-Test` (e.g., `OrderManagement-Test`)
3. Supported account types: **Single tenant**
4. Redirect URI: Leave blank (ROPC doesn't need one)
5. After creation, note the **Application (client) ID** and **Directory (tenant) ID**

### Configure Authentication

1. **Authentication** → **Allow public client flows** → **Yes** (required for ROPC; no client secret is needed)

### Define App Roles

1. **App roles** → **Create app role** for each permission:

| Display Name | Value | Allowed members |
|-------------|-------|-----------------|
| Create Customers | `customers:create` | Users/Groups |
| Create Products | `products:create` | Users/Groups |
| Manage Stock | `products:manage-stock` | Users/Groups |
| Create Orders | `orders:create` | Users/Groups |
| Submit Orders | `orders:submit` | Users/Groups |
| Approve Orders | `orders:approve` | Users/Groups |
| Ship Orders | `orders:ship` | Users/Groups |
| Deliver Orders | `orders:deliver` | Users/Groups |
| Cancel Orders | `orders:cancel` | Users/Groups |
| Read Orders | `orders:read` | Users/Groups |
| Read All Orders | `orders:read-all` | Users/Groups |

## Step 3: Create Test Users

1. **Users** → **New user** → **Create new user**
2. Create users matching your role model:

| User | Username | Assigned Roles |
|------|----------|---------------|
| Sales Rep | `salesrep@contoso-test.onmicrosoft.com` | `customers:create`, `orders:create`, `orders:submit`, `orders:cancel`, `orders:read` |
| Warehouse Manager | `warehouse@contoso-test.onmicrosoft.com` | `products:create`, `products:manage-stock`, `orders:approve`, `orders:ship`, `orders:deliver`, `orders:read-all` |
| Admin | `admin@contoso-test.onmicrosoft.com` | All roles |

3. Set a known password for each user (disable "require password change at first sign-in")
4. **Enterprise Applications** → Select your app → **Users and groups** → Assign each user with their roles

## Step 4: Disable MFA for Test Users

ROPC does not support interactive MFA. In the test tenant:

1. **Security** → **Conditional Access** → Create a policy that **excludes** test users from MFA
2. Or use **Per-user MFA** settings to disable MFA for test accounts

## Step 5: Configure Secrets

### Local Development — `dotnet user-secrets`

Use the .NET Secret Manager to store credentials locally. Secrets are stored outside the project tree and never committed to source control.

```bash
# Initialize user secrets in the API test project
cd Api/tests
dotnet user-secrets init

# Store Entra test tenant configuration
dotnet user-secrets set "EntraTest:TenantId" "<your-test-tenant-id>"
dotnet user-secrets set "EntraTest:ClientId" "<your-app-client-id>"
dotnet user-secrets set "EntraTest:Scopes:0" "api://<your-app-client-id>/.default"

# Store test user credentials
dotnet user-secrets set "EntraTest:TestUsers:salesRep:Username" "salesrep@contoso-test.onmicrosoft.com"
dotnet user-secrets set "EntraTest:TestUsers:salesRep:Password" "<salesrep-password>"
dotnet user-secrets set "EntraTest:TestUsers:warehouseManager:Username" "warehouse@contoso-test.onmicrosoft.com"
dotnet user-secrets set "EntraTest:TestUsers:warehouseManager:Password" "<warehouse-password>"
dotnet user-secrets set "EntraTest:TestUsers:admin:Username" "admin@contoso-test.onmicrosoft.com"
dotnet user-secrets set "EntraTest:TestUsers:admin:Password" "<admin-password>"
```

### Bind to `MsalTestOptions` from Configuration

In your test fixture, build a configuration and bind directly to `MsalTestOptions`:

```csharp
var configuration = new ConfigurationBuilder()
    .AddUserSecrets<TestWebApplicationFactoryFixture>()  // local secrets
    .AddEnvironmentVariables()                            // CI/CD override
    .Build();

var msalOptions = new MsalTestOptions();
configuration.GetSection("EntraTest").Bind(msalOptions);
```

This works because `MsalTestOptions` property names match the configuration keys:
- `EntraTest:TenantId` → `MsalTestOptions.TenantId`
- `EntraTest:TestUsers:salesRep:Username` → `MsalTestOptions.TestUsers["salesRep"].Username`

You can also set `ExpectedPermissions` for test assertions:

```bash
dotnet user-secrets set "EntraTest:TestUsers:salesRep:ExpectedPermissions:0" "customers:create"
dotnet user-secrets set "EntraTest:TestUsers:salesRep:ExpectedPermissions:1" "orders:create"
dotnet user-secrets set "EntraTest:TestUsers:salesRep:ExpectedPermissions:2" "orders:submit"
dotnet user-secrets set "EntraTest:TestUsers:salesRep:ExpectedPermissions:3" "orders:cancel"
dotnet user-secrets set "EntraTest:TestUsers:salesRep:ExpectedPermissions:4" "orders:read"
```

## Step 6: Write E2E Tests

```csharp
public class OrderE2ETests : IClassFixture<TestWebApplicationFactoryFixture>
{
    private readonly TestWebApplicationFactoryFixture _factory;
    private readonly MsalTestTokenProvider? _tokenProvider;

    public OrderE2ETests(TestWebApplicationFactoryFixture factory)
    {
        _factory = factory;

        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<TestWebApplicationFactoryFixture>()
            .AddEnvironmentVariables()
            .Build();

        var msalOptions = new MsalTestOptions();
        configuration.GetSection("EntraTest").Bind(msalOptions);

        if (!string.IsNullOrEmpty(msalOptions.TenantId))
            _tokenProvider = new MsalTestTokenProvider(msalOptions);
    }

    [Fact]
    public async Task SalesRep_CanCreateOrder()
    {
        Skip.If(_tokenProvider is null, "Entra test tenant not configured");

        var client = await _factory.CreateClientWithEntraTokenAsync(_tokenProvider, "salesRep");

        var response = await client.PostAsJsonAsync("/api/orders?api-version=2026-11-12", new
        {
            customerId = "...",
            lineItems = new[] { new { productId = "...", quantity = 2 } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SalesRep_CannotApproveOrder()
    {
        Skip.If(_tokenProvider is null, "Entra test tenant not configured");

        var client = await _factory.CreateClientWithEntraTokenAsync(_tokenProvider, "salesRep");

        var response = await client.PostAsync($"/api/orders/{orderId}/approval?api-version=2026-11-12", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
```

## CI/CD Integration

In CI/CD, use environment variables (they take precedence over user-secrets via `AddEnvironmentVariables()`). The configuration binding expects the `EntraTest__` prefix with double underscores for nested keys.

### GitHub Actions

```yaml
env:
  EntraTest__TenantId: ${{ secrets.ENTRA_TEST_TENANT_ID }}
  EntraTest__ClientId: ${{ secrets.ENTRA_TEST_CLIENT_ID }}
  EntraTest__Scopes__0: "api://${{ secrets.ENTRA_TEST_CLIENT_ID }}/.default"
  EntraTest__TestUsers__salesRep__Username: ${{ secrets.ENTRA_TEST_SALESREP_USERNAME }}
  EntraTest__TestUsers__salesRep__Password: ${{ secrets.ENTRA_TEST_SALESREP_PASSWORD }}
  EntraTest__TestUsers__warehouseManager__Username: ${{ secrets.ENTRA_TEST_WAREHOUSE_USERNAME }}
  EntraTest__TestUsers__warehouseManager__Password: ${{ secrets.ENTRA_TEST_WAREHOUSE_PASSWORD }}
  EntraTest__TestUsers__admin__Username: ${{ secrets.ENTRA_TEST_ADMIN_USERNAME }}
  EntraTest__TestUsers__admin__Password: ${{ secrets.ENTRA_TEST_ADMIN_PASSWORD }}

steps:
  - name: Run E2E Tests
    run: dotnet test --filter "Category=E2E"
```

### Skip When Not Configured

Tests gracefully skip when Entra credentials are absent (no user-secrets and no env vars):

```csharp
Skip.If(_tokenProvider is null, "Entra test tenant not configured");
```

## Troubleshooting

| Issue | Cause | Fix |
|-------|-------|-----|
| `AADSTS50126: Invalid username or password` | Wrong credentials | Verify password, ensure no password change required |
| `AADSTS50076: MFA required` | MFA enabled for user | Disable MFA or exclude from CA policy |
| `AADSTS7000218: Request body must contain client_assertion or client_secret` | App not configured as public client | Set **Allow public client flows** → **Yes** in app registration |
| `AADSTS650057: Invalid resource` | Wrong scope | Use `api://{clientId}/.default` |
| Empty permissions in `Actor` | Roles not assigned | Assign app roles to users in Enterprise Applications |
