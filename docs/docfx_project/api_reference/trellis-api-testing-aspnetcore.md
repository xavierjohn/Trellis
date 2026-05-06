---
package: Trellis.Testing.AspNetCore
namespaces: [Trellis.Testing.AspNetCore, Trellis.Testing.AspNetCore.Http]
types: [WebApplicationFactoryExtensions, WebApplicationFactoryTimeExtensions, ServiceCollectionExtensions, ServiceCollectionDbProviderExtensions, MsalTestTokenProvider, MsalTestOptions, TestUserCredentials, HttpFileParser, HttpFileRunner, HttpFileAssertions, HttpFileTheoryData, HttpFileRequest, HttpFileResult, ExpectedOutcome, HttpFileAssertionException, ScenarioContext]
version: v3
last_verified: 2026-05-06
audience: [llm]
---
# Trellis.Testing.AspNetCore &mdash; API Reference

**Package:** `Trellis.Testing.AspNetCore`  
**Namespaces:** `Trellis.Testing.AspNetCore`, `Trellis.Testing.AspNetCore.Http`  
**Purpose:** ASP.NET Core integration-test utilities for `WebApplicationFactory<TEntryPoint>`, dependency replacement, fake time, MSAL-backed Entra E2E tokens, and replaying `.http` files.

Use this package from test projects only. It depends on `Microsoft.AspNetCore.Mvc.Testing`, EF Core, MSAL, `Trellis.Authorization`, and `Trellis.Testing`.

## Use this file when

- You are writing ASP.NET Core integration tests with `WebApplicationFactory<TEntryPoint>`.
- You need to replace services, resource loaders, DB providers, time providers, or actor headers in tests.
- You want to replay `.http` files against a test host and assert expected status/header behavior.

## Patterns Index

| Goal | Canonical API / pattern | See |
|---|---|---|
| Create a client with `X-Test-Actor` | `factory.CreateClientWithActor(...)` | [`WebApplicationFactoryExtensions`](#webapplicationfactoryextensions) |
| Replace an app service before the host is built | `WithWebHostBuilder(...ConfigureTestServices(s => s.ReplaceSingleton<T>(fake)))` | [`ServiceCollectionExtensions`](#servicecollectionextensions) |
| Replace a resource loader | `services.ReplaceResourceLoader<TMessage,TResource>(...)` | [`ServiceCollectionExtensions`](#servicecollectionextensions) |
| Swap EF provider for integration tests | `services.ReplaceDbProvider<TContext>(...)` | [`ServiceCollectionDbProviderExtensions`](#servicecollectiondbproviderextensions) |
| Use deterministic time | `factory.WithFakeTimeProvider(...)` | [`WebApplicationFactoryTimeExtensions`](#webapplicationfactorytimeextensions) |
| Acquire a real Entra token in gated E2E tests | `MsalTestTokenProvider` + `CreateClientWithEntraTokenAsync(...)` | [`MsalTestTokenProvider`](#msaltesttokenprovider) |
| Replay `.http` files | `HttpFileParser.ParseFile`, `HttpFileRunner.RunAsync`, `HttpFileAssertions.AssertExpectationsMet` | [`.http` file replay helpers](#http-file-replay-helpers) |

## API failure-path test checklist

- Include `.http` requests for the success path and representative failure paths (`422`, `409`, `403`, `404`, and framework-level `400` when applicable).
- Keep expected statuses close to the request with `# @expect status: ...` so replay tests and human examples stay in sync.
- Use `CreateClientWithActor(...)` for authorization paths instead of hand-rolling actor headers.
- Replace services through `ConfigureTestServices` before the host is built; do not mutate the service collection after `CreateClient()`.

## WebApplicationFactory helpers

### `WebApplicationFactoryExtensions`

```csharp
public static class WebApplicationFactoryExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static HttpClient CreateClientWithActor<TEntryPoint>(this WebApplicationFactory<TEntryPoint> factory, string actorId, params string[] permissions) where TEntryPoint : class` | `HttpClient` | Creates a client and writes an `X-Test-Actor` header containing the actor id, permissions, empty forbidden-permissions list, and empty attributes object. Use with the ASP development/test actor provider. |
| `public static HttpClient CreateClientWithActor<TEntryPoint>(this WebApplicationFactory<TEntryPoint> factory, Actor actor) where TEntryPoint : class` | `HttpClient` | Creates a client and serializes the full `Actor` into `X-Test-Actor`, including forbidden permissions and attributes. |
| `[RequiresUnreferencedCode] public static Task<HttpClient> CreateClientWithEntraTokenAsync<TEntryPoint>(this WebApplicationFactory<TEntryPoint> factory, MsalTestTokenProvider tokenProvider, string testUserName, CancellationToken cancellationToken = default) where TEntryPoint : class` | `Task<HttpClient>` | Acquires a real Entra ID token with `MsalTestTokenProvider`, creates a client, and sets the `Authorization: Bearer <token>` header. Use only in E2E tests against a dedicated test tenant. |

### `WebApplicationFactoryTimeExtensions`

```csharp
public static class WebApplicationFactoryTimeExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static readonly DateTimeOffset DefaultTestStartInstant` | `DateTimeOffset` | Deterministic default fake-clock baseline: `2024-01-01T00:00:00Z`. |
| `public static WebApplicationFactory<TEntryPoint> WithFakeTimeProvider<TEntryPoint>(this WebApplicationFactory<TEntryPoint> factory, FakeTimeProvider fakeTimeProvider) where TEntryPoint : class` | `WebApplicationFactory<TEntryPoint>` | Returns a factory configured with the supplied `FakeTimeProvider` as the singleton `TimeProvider`. |
| `public static WebApplicationFactory<TEntryPoint> WithFakeTimeProvider<TEntryPoint>(this WebApplicationFactory<TEntryPoint> factory, out FakeTimeProvider fakeTimeProvider) where TEntryPoint : class` | `WebApplicationFactory<TEntryPoint>` | Creates a `FakeTimeProvider` at `DefaultTestStartInstant`, registers it, and returns it through the `out` parameter. |
| `public static WebApplicationFactory<TEntryPoint> WithFakeTimeProvider<TEntryPoint>(this WebApplicationFactory<TEntryPoint> factory, DateTimeOffset startInstant, out FakeTimeProvider fakeTimeProvider) where TEntryPoint : class` | `WebApplicationFactory<TEntryPoint>` | Creates a `FakeTimeProvider` at the supplied instant, registers it, and returns it through the `out` parameter. |

## Dependency replacement helpers

### `ServiceCollectionExtensions`

```csharp
public static class ServiceCollectionExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IServiceCollection ReplaceResourceLoader<TMessage, TResource>(this IServiceCollection services, Func<IServiceProvider, IResourceLoader<TMessage, TResource>> factory)` | `IServiceCollection` | Removes existing `IResourceLoader<TMessage,TResource>` registrations and adds the supplied scoped factory. |
| `public static IServiceCollection ReplaceSingleton<TService>(this IServiceCollection services, TService instance) where TService : class` | `IServiceCollection` | Removes all registrations of `TService` and registers the supplied singleton instance. |

### `ServiceCollectionDbProviderExtensions`

```csharp
public static class ServiceCollectionDbProviderExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IServiceCollection ReplaceDbProvider<TContext>(this IServiceCollection services, Action<DbContextOptionsBuilder> configureOptions) where TContext : DbContext` | `IServiceCollection` | Removes the existing `TContext`, `DbContextOptions<TContext>`, and EF Core provider-scoped services for that context, then re-registers `TContext` with `AddDbContext<TContext>(configureOptions)`. Use for swapping production providers to SQLite/in-memory providers in integration tests. |

`TContext` carries `[DynamicallyAccessedMembers(PublicConstructors | NonPublicConstructors | PublicProperties)]` for EF Core compatibility.

## MSAL / Entra E2E token helpers

### `MsalTestOptions`

```csharp
public sealed class MsalTestOptions
```

| Member | Type | Description |
| --- | --- | --- |
| `TenantId` | `string` | Azure Entra tenant id or domain. |
| `ClientId` | `string` | Public-client app registration id. The app must allow public client flows for ROPC. |
| `Scopes` | `string[]` | Scopes requested when acquiring tokens. |
| `TestUsers` | `Dictionary<string, TestUserCredentials>` | Named test users keyed by logical role name, e.g. `"salesRep"`. |

### `TestUserCredentials`

```csharp
public sealed class TestUserCredentials
```

| Member | Type | Description |
| --- | --- | --- |
| `Username` | `string` | User principal name or email. |
| `Password` | `string` | Test-user password. Store outside source control. |
| `ExpectedPermissions` | `string[]` | Permissions the test user is expected to have after authentication. |

### `MsalTestTokenProvider`

```csharp
[RequiresUnreferencedCode("MSAL uses reflection for token serialization and is not AOT-compatible.")]
public sealed class MsalTestTokenProvider
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public MsalTestTokenProvider(MsalTestOptions options)` | — | Creates a public MSAL client for `options.ClientId` and `options.TenantId`. |
| `public Task<string> AcquireTokenAsync(string testUserName, CancellationToken cancellationToken = default)` | `Task<string>` | Acquires an access token for the named user via MSAL ROPC. Throws `KeyNotFoundException` if `testUserName` is not configured and `MsalException` when token acquisition fails. |

ROPC is deprecated for production use. Use this helper only for gated E2E tests against a dedicated test tenant with MFA disabled for test users.

## `.http` file replay helpers

These types live in `namespace Trellis.Testing.AspNetCore.Http`.

### `HttpFileParser`

```csharp
public static class HttpFileParser
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IReadOnlyList<HttpFileRequest> Parse(string content, IReadOnlyDictionary<string, string>? vars = null)` | `IReadOnlyList<HttpFileRequest>` | Parses raw `.http` content. File-level `@var = value` entries and supplied `vars` are substituted immediately; response placeholders are left for the runner. |
| `public static IReadOnlyList<HttpFileRequest> ParseFile(string path, IReadOnlyDictionary<string, string>? vars = null)` | `IReadOnlyList<HttpFileRequest>` | Reads and parses a `.http` file from disk. |

Supported syntax: `###` request separators, `# @name`, `# @expect status: 201`, `# @expect status: 2xx`, `# @expect status: 200-299`, `# @expect header: ETag`, `# @parity: status-only`, file variables, `{{var}}`, `{{name.response.body.path}}`, `{{name.response.headers.Header-Name}}`, and `{{name.response.status}}`.

### `HttpFileRunner`

```csharp
public static class HttpFileRunner
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Task<IReadOnlyList<HttpFileResult>> RunAsync(HttpClient client, IReadOnlyList<HttpFileRequest> requests, CancellationToken ct = default)` | `Task<IReadOnlyList<HttpFileResult>>` | Executes parsed requests in order, sharing a `ScenarioContext` so named responses can feed later substitutions. |
| `public static Task<HttpFileResult> RunSingleAsync(HttpClient client, HttpFileRequest request, ScenarioContext context, CancellationToken ct = default)` | `Task<HttpFileResult>` | Executes one request after resolving deferred response placeholders, records named responses, reads the response body as text, and returns the result. |

### `HttpFileAssertions`

```csharp
public static class HttpFileAssertions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static void AssertExpectationsMet(HttpFileResult result)` | `void` | Enforces the request's `ExpectedOutcome`. If no expectations were declared, asserts the status is in the non-error range `100`-`399`. Throws `HttpFileAssertionException` on failure. |

### Data records and support types

| Type | Declaration | Description |
| --- | --- | --- |
| `ExpectedOutcome` | `public sealed record ExpectedOutcome(int? StatusMin, int? StatusMax, IReadOnlyList<string> RequiredHeaders)` | Parsed `# @expect` status/header assertions. |
| `HttpFileRequest` | `public sealed record HttpFileRequest(string Title, string Method, string Url, IReadOnlyDictionary<string, string> Headers, string? Body, string? Name, ExpectedOutcome? Expected, string? ParityMode = null)` | One parsed request. `Url` and `Body` may contain deferred response placeholders. |
| `HttpFileResult` | `public sealed record HttpFileResult(HttpFileRequest Request, HttpResponseMessage Response, string? Body, ExpectedOutcome? Expected)` | One executed request and response. Caller owns `Response` disposal. |
| `HttpFileTheoryData` | `public static class HttpFileTheoryData` | Provides `FromFile(string path, IReadOnlyDictionary<string,string>? vars = null) : IEnumerable<object[]>` for xUnit-style member data without taking an xUnit dependency. |
| `ScenarioContext` | `public sealed class ScenarioContext` | Records named responses and resolves `{{name.response.*}}` tokens. Public members: `Record(...)` and `TryResolve(...)`. |
| `HttpFileAssertionException` | `public sealed class HttpFileAssertionException : Exception` | Thrown by `HttpFileAssertions` with constructors `()`, `(string message)`, and `(string message, Exception inner)`. |

## Common examples

### Authenticated in-process client

```csharp
using Trellis.Testing.AspNetCore;

var client = factory.CreateClientWithActor("user-1", "orders:read", "orders:write");
```

### Replace EF provider and fake time

```csharp
using Microsoft.EntityFrameworkCore;
using Trellis.Testing.AspNetCore;

factory = factory.WithFakeTimeProvider(out var fakeTime);
fakeTime.SetUtcNow(WebApplicationFactoryTimeExtensions.DefaultTestStartInstant.AddDays(3));

builder.ConfigureServices(services =>
    services.ReplaceDbProvider<AppDbContext>(options =>
        options.UseSqlite(connection)));
```

### Replay a `.http` file

```csharp
using Trellis.Testing.AspNetCore.Http;

var requests = HttpFileParser.ParseFile("Scenarios/orders.http", new Dictionary<string, string>
{
    ["baseUrl"] = factory.Server.BaseAddress!.ToString().TrimEnd('/')
});

var results = await HttpFileRunner.RunAsync(client, requests, cancellationToken);

foreach (var result in results)
    HttpFileAssertions.AssertExpectationsMet(result);
```

## See also

- [trellis-api-testing-reference.md](trellis-api-testing-reference.md) — core Trellis testing assertions, fake repositories, and test actors.
- [trellis-api-authorization.md](trellis-api-authorization.md) — `Actor`, `IActorProvider`, resource authorization contracts.
- [trellis-api-asp.md](trellis-api-asp.md) — ASP.NET Core response mapping and actor providers.
