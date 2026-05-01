---
title: Testing With Azure Entra ID Tokens
package: Trellis.Testing.AspNetCore
topics: [testing, entra, msal, ropc, integration-test, webfactory, e2e, actor]
related_api_reference: [trellis-api-testing-aspnetcore.md, trellis-api-asp.md, trellis-api-authorization.md]
last_verified: 2026-05-01
audience: [developer]
---
# Testing With Azure Entra ID Tokens

`Trellis.Testing.AspNetCore` ships an MSAL-backed token provider so a small set of E2E integration tests can drive the *real* authentication path — Entra issuance, JWT validation, claim mapping, and `EntraActorProvider` — instead of bypassing it with `X-Test-Actor`.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Fast hermetic auth tests (no network, no clock) | `factory.CreateClientWithActor(actorId, permissions...)` | [Choosing a client helper](#choosing-a-client-helper) |
| Acquire a real Entra token by named test user | `new MsalTestTokenProvider(options).AcquireTokenAsync(testUserName, ct)` | [Token provider](#token-provider) |
| Build an `HttpClient` with `Authorization: Bearer <token>` | `factory.CreateClientWithEntraTokenAsync(tokenProvider, testUserName, ct)` | [Authenticated test client](#authenticated-test-client) |
| Bind tenant + scopes + test users from configuration | `MsalTestOptions` + `IConfiguration.Bind` | [Configuration](#configuration) |
| Share token cache across tests in a class | One `MsalTestTokenProvider` per `IClassFixture` | [Fixture pattern](#fixture-pattern) |
| Gate slow E2E tests out of inner-loop runs | `[Trait("Category", "E2E")]` (xUnit) | [Practical guidance](#practical-guidance) |

## Use this guide when

- You already have hermetic header-based authorization tests via `CreateClientWithActor` and want a *small* additional suite that exercises real Entra issuance and JWT validation end-to-end.
- You need to verify that `EntraActorProvider` (and your `MapPermissions` / `MapAttributes` overrides) project a real Entra token into the `Actor` you expect.
- You operate a dedicated Entra **test** tenant whose lifecycle you control — separate app registration, separate test users, MFA disabled.
- You can keep credentials in user-secrets locally and CI secrets in pipelines — never in source.

## Surface at a glance

The Entra E2E surface is three types in `Trellis.Testing.AspNetCore` plus one extension method on `WebApplicationFactory<TEntryPoint>`.

| API | Kind | Returns | Purpose |
|---|---|---|---|
| `MsalTestOptions` | `sealed class` | — | `TenantId`, `ClientId`, `Scopes`, `TestUsers` (named credentials). Bind from `IConfiguration`. |
| `TestUserCredentials` | `sealed class` | — | `Username`, `Password`, `ExpectedPermissions` for one named test user. |
| `MsalTestTokenProvider(MsalTestOptions options)` | ctor | — | Builds a public-client MSAL app for `ClientId`/`TenantId` on `AzureCloudInstance.AzurePublic`. |
| `MsalTestTokenProvider.AcquireTokenAsync(string testUserName, CancellationToken)` | instance | `Task<string>` | ROPC token acquisition. MSAL caches results per provider instance. Throws `KeyNotFoundException` if the user is not configured, `MsalException` on acquisition failure. |
| `WebApplicationFactoryExtensions.CreateClientWithEntraTokenAsync<TEntryPoint>(factory, tokenProvider, testUserName, ct)` | extension | `Task<HttpClient>` | Acquires a token via the provider and sets `Authorization: Bearer <token>` on a fresh client. |

`MsalTestTokenProvider` and `CreateClientWithEntraTokenAsync` are annotated `[RequiresUnreferencedCode]` because MSAL relies on reflection and is not AOT-compatible.

Full signatures: [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md).

## Installation

```bash
dotnet add package Trellis.Testing.AspNetCore
```

The package depends on `Microsoft.AspNetCore.Mvc.Testing`, `Trellis.Authorization`, and MSAL (`Microsoft.Identity.Client`). Reference it from test projects only.

## Quick start

Bind `MsalTestOptions` from configuration, hand the provider to `CreateClientWithEntraTokenAsync`, then call your real API exactly as production callers would.

```csharp
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Trellis.Testing.AspNetCore;
using Xunit;

[JsonSerializable(typeof(CreateOrderRequest))]
internal partial class ApiJsonContext : JsonSerializerContext { }

public sealed record CreateOrderRequest(string CustomerId, int Quantity);

public sealed class Program { }

public sealed class OrdersEntraTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly MsalTestTokenProvider _tokens;

    public OrdersEntraTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;

        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<OrdersEntraTests>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new MsalTestOptions();
        configuration.GetSection("EntraTest").Bind(options);
        _tokens = new MsalTestTokenProvider(options);
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task Sales_rep_can_create_orders()
    {
        var client = await _factory.CreateClientWithEntraTokenAsync(_tokens, "salesRep");

        var response = await client.PostAsJsonAsync(
            "/api/orders",
            new CreateOrderRequest("customer-1", 2),
            ApiJsonContext.Default.CreateOrderRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
```

Commands invoked behind `/api/orders` still return `Result<Unit>` — the assertion is on the HTTP status produced by `Trellis.Asp` mapping.

## Choosing a client helper

| Helper | Path exercised | Network? | Determinism |
|---|---|---|---|
| `factory.CreateClientWithActor(actorId, perms...)` | Test actor provider → `Actor` | None (in-process) | Fully deterministic |
| `factory.CreateClientWithActor(actor)` | Same, with `ForbiddenPermissions` and `Attributes` | None | Fully deterministic |
| `factory.CreateClientWithEntraTokenAsync(provider, testUserName, ct)` | Entra ID → JWT validation → `EntraActorProvider` → `Actor` | Live calls to Microsoft Entra | Non-deterministic (network + token expiry) |

Use `CreateClientWithActor` for the bulk of authorization coverage (handler logic, command rules, `IAuthorize`, `IAuthorizeResource<T>`). Reserve `CreateClientWithEntraTokenAsync` for a small confidence suite that proves real tokens still flow into the `Actor` you expect.

`CreateClientWithActor` is documented in [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md#webapplicationfactoryextensions); `EntraActorProvider` semantics live in [`integration-asp-authorization.md`](integration-asp-authorization.md#entra-id-provider).

## Tenant prerequisites

`MsalTestTokenProvider` uses MSAL ROPC. ROPC is intentionally restricted by Entra and requires a tenant configured for it.

| Requirement | Why |
|---|---|
| Dedicated test tenant | Never reuse a production tenant for credential-based test flows. |
| App registration: single tenant, **Allow public client flows = Yes** | ROPC is a public-client grant; a confidential client cannot use it. |
| Test users created in the tenant | ROPC needs a real user principal. |
| MFA disabled for test users | ROPC cannot perform interactive MFA challenges. |
| App roles assigned to test users via Enterprise Application | Without role assignments, tokens authenticate but carry no `roles` claims. |
| Scope: `api://<clientId>/.default` (or your custom scope) | ROPC fails with `AADSTS650057` on an unknown scope. |

> [!WARNING]
> ROPC is deprecated for production authentication. Use it only for automated tests against a dedicated test tenant whose users are excluded from MFA.

## Configuration

Bind `MsalTestOptions` from any `IConfiguration` source. Locally, prefer `dotnet user-secrets`; in CI, environment variables with the `EntraTest__` prefix.

### Local user-secrets

```bash
dotnet user-secrets set "EntraTest:TenantId" "<tenant-id>"
dotnet user-secrets set "EntraTest:ClientId" "<client-id>"
dotnet user-secrets set "EntraTest:Scopes:0" "api://<client-id>/.default"

dotnet user-secrets set "EntraTest:TestUsers:salesRep:Username" "salesrep@contoso-test.onmicrosoft.com"
dotnet user-secrets set "EntraTest:TestUsers:salesRep:Password" "<password>"
dotnet user-secrets set "EntraTest:TestUsers:salesRep:ExpectedPermissions:0" "orders:create"
dotnet user-secrets set "EntraTest:TestUsers:salesRep:ExpectedPermissions:1" "orders:read"
```

### CI environment variables

```yaml
env:
  EntraTest__TenantId: ${{ secrets.ENTRA_TEST_TENANT_ID }}
  EntraTest__ClientId: ${{ secrets.ENTRA_TEST_CLIENT_ID }}
  EntraTest__Scopes__0: api://${{ secrets.ENTRA_TEST_CLIENT_ID }}/.default
  EntraTest__TestUsers__salesRep__Username: ${{ secrets.ENTRA_TEST_SALESREP_USERNAME }}
  EntraTest__TestUsers__salesRep__Password: ${{ secrets.ENTRA_TEST_SALESREP_PASSWORD }}
```

### Binding code

```csharp
using Microsoft.Extensions.Configuration;
using Trellis.Testing.AspNetCore;

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var options = new MsalTestOptions();
configuration.GetSection("EntraTest").Bind(options);

if (string.IsNullOrWhiteSpace(options.TenantId) || string.IsNullOrWhiteSpace(options.ClientId))
    throw new InvalidOperationException("Configure EntraTest:TenantId and EntraTest:ClientId before running Entra E2E tests.");

var tokenProvider = new MsalTestTokenProvider(options);
```

## Token provider

`MsalTestTokenProvider` wraps an MSAL `IPublicClientApplication`. The constructor builds it once for the configured `ClientId`/`TenantId`; `AcquireTokenAsync` performs ROPC against the named user.

| Concern | Behavior |
|---|---|
| Token cache | MSAL caches per provider instance — keep one provider per fixture or class. |
| Unknown user name | `KeyNotFoundException` listing configured user keys. |
| Acquisition failure | `MsalException` (e.g. `AADSTS50126` invalid credentials, `AADSTS50076` MFA required, `AADSTS7000218` not a public client). |
| AOT | `[RequiresUnreferencedCode]` — MSAL is not trim/AOT-compatible. |
| Cancellation | `CancellationToken` flows into `ExecuteAsync`. |

```csharp
using System.Threading;
using System.Threading.Tasks;
using Trellis.Testing.AspNetCore;

public sealed class TokenSmokeTests
{
    [Fact]
    [Trait("Category", "E2E")]
    public async Task Provider_returns_non_empty_token_for_named_user()
    {
        var options = new MsalTestOptions
        {
            TenantId = Environment.GetEnvironmentVariable("EntraTest__TenantId")!,
            ClientId = Environment.GetEnvironmentVariable("EntraTest__ClientId")!,
            Scopes = [$"api://{Environment.GetEnvironmentVariable("EntraTest__ClientId")}/.default"],
            TestUsers =
            {
                ["salesRep"] = new TestUserCredentials
                {
                    Username = Environment.GetEnvironmentVariable("EntraTest__TestUsers__salesRep__Username")!,
                    Password = Environment.GetEnvironmentVariable("EntraTest__TestUsers__salesRep__Password")!,
                    ExpectedPermissions = ["orders:create", "orders:read"],
                }
            }
        };

        var provider = new MsalTestTokenProvider(options);

        var token = await provider.AcquireTokenAsync("salesRep", CancellationToken.None);

        token.Should().NotBeNullOrWhiteSpace();
    }
}
```

## Authenticated test client

`CreateClientWithEntraTokenAsync` is a thin glue method: acquire a token, build a fresh `HttpClient` from the factory, set `Authorization: Bearer <token>`. Use it whenever you would have written that boilerplate by hand.

```csharp
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Trellis.Testing.AspNetCore;
using Xunit;

public sealed class Program { }

public sealed class OrdersAuthenticationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly MsalTestTokenProvider _tokens;

    public OrdersAuthenticationTests(WebApplicationFactory<Program> factory, MsalTestTokenProvider tokens)
    {
        _factory = factory;
        _tokens = tokens;
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task Bearer_token_reaches_protected_endpoint()
    {
        var client = await _factory.CreateClientWithEntraTokenAsync(_tokens, "salesRep");

        var response = await client.GetAsync("/api/orders");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
```

## Fixture pattern

MSAL caches tokens per provider instance, so share **one** `MsalTestTokenProvider` across every test that needs the same credentials. xUnit's `IClassFixture<T>` is the simplest seam.

```csharp
using System;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Trellis.Testing.AspNetCore;
using Xunit;

public sealed class Program { }

public sealed class EntraTestFixture : IDisposable
{
    public EntraTestFixture()
    {
        Factory = new WebApplicationFactory<Program>();

        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<EntraTestFixture>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new MsalTestOptions();
        configuration.GetSection("EntraTest").Bind(options);

        if (string.IsNullOrWhiteSpace(options.TenantId) || string.IsNullOrWhiteSpace(options.ClientId))
            throw new InvalidOperationException("Configure EntraTest before running Entra E2E tests.");

        Tokens = new MsalTestTokenProvider(options);
        Options = options;
    }

    public WebApplicationFactory<Program> Factory { get; }
    public MsalTestTokenProvider Tokens { get; }
    public MsalTestOptions Options { get; }

    public void Dispose() => Factory.Dispose();
}

public sealed class OrdersScenarioTests : IClassFixture<EntraTestFixture>
{
    private readonly EntraTestFixture _fx;

    public OrdersScenarioTests(EntraTestFixture fx) => _fx = fx;

    [Fact]
    [Trait("Category", "E2E")]
    public async Task Admin_can_list_orders()
    {
        var client = await _fx.Factory.CreateClientWithEntraTokenAsync(_fx.Tokens, "admin");
        var response = await client.GetAsync("/api/orders");
        response.IsSuccessStatusCode.Should().BeTrue();
    }
}
```

`TestUserCredentials.ExpectedPermissions` is the right place to record what each named user is *supposed* to receive — assert it once per fixture so a missing role assignment in the tenant fails noisily rather than silently producing 403s in unrelated tests.

## Common ROPC failure codes

| MSAL error | Usually means | Fix |
|---|---|---|
| `AADSTS50126` | Invalid username or password | Reset the test-user password; re-bind secret. |
| `AADSTS50076` | MFA required | Exclude test users from MFA in the test tenant. |
| `AADSTS7000218` | App registration is not a public client | Set "Allow public client flows" to **Yes**. |
| `AADSTS650057` | Invalid scope | Use `api://<clientId>/.default` (or the custom scope you exposed). |
| Token issued, app sees no permissions | App roles not assigned to the user | Assign roles via Enterprise Applications → Users and groups. |

## Composition

Entra E2E tests sit alongside the rest of the Trellis test surface, not on top of it.

- **With `CreateClientWithActor`.** Cover authorization rules with the header-based helper; cover *real-token plumbing* with `CreateClientWithEntraTokenAsync`. They share the same `WebApplicationFactory<TEntryPoint>` and assertion vocabulary.
- **With `WithFakeTimeProvider` / `ReplaceDbProvider`.** Both still apply when an Entra-authenticated client is in play — fake the clock, swap the EF provider, then call `CreateClientWithEntraTokenAsync` against the modified factory. See [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md#dependency-replacement-helpers).
- **With `EntraActorProvider`.** The provider under test is the same one wired in production via `AddEntraActorProvider(...)`; an Entra E2E test is the only place where overrides to `MapPermissions` / `MapAttributes` are exercised against real claim shapes. See [`integration-asp-authorization.md`](integration-asp-authorization.md#customizing-claim-mapping).
- **With Trellis result pipelines.** Commands invoked behind authenticated endpoints still return `Result<Unit>`; assertions remain on HTTP status mapped by `Trellis.Asp`.

## Practical guidance

- **Keep this suite small.** A handful of scenarios per role is enough to prove the auth pipeline works. Duplicating every authorization test against a real tenant is waste.
- **Gate with a trait.** `[Trait("Category", "E2E")]` (xUnit) lets local runs use `--filter "Category!=E2E"` and keeps inner-loop builds fast and offline.
- **One provider per fixture.** MSAL caches by provider instance — sharing a provider across tests in a class costs one network round-trip per token, not one per test.
- **Treat the test tenant as disposable infrastructure.** Script the app registration and role assignments; do not rely on manual portal edits.
- **Never log access tokens.** They are valid bearer credentials against your test tenant. Log only the resulting HTTP status and (if needed) decoded `oid`/`roles` claims.
- **Bind options once.** Reading `IConfiguration` and constructing the provider in fixture setup keeps tests readable and prevents per-test token thrash.
- **Prefer `dotnet user-secrets` locally.** Environment variables are fine for CI but leak into shell history.

## Cross-references

- API surface: [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md)
- Header-based test client (`CreateClientWithActor`) and `Actor` shape: [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md#webapplicationfactoryextensions)
- Production `EntraActorProvider`, `MapPermissions`, `MapAttributes`: [`integration-asp-authorization.md`](integration-asp-authorization.md) and [`trellis-api-authorization.md`](../api_reference/trellis-api-authorization.md)
- ASP response mapping (status codes the assertions check against): [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md)
