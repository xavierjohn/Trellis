# Trellis.Testing.AspNetCore

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Testing.AspNetCore.svg)](https://www.nuget.org/packages/Trellis.Testing.AspNetCore)

ASP.NET Core integration test utilities for Trellis applications.

## Installation
```bash
dotnet add package Trellis.Testing.AspNetCore
```

## Quick Example
```csharp
using Trellis.Testing.AspNetCore;

// Create an authenticated test client
var client = _factory.CreateClientWithActor("user-1", "Orders.Read", "Orders.Write");

// Swap the database provider for tests
builder.ConfigureServices(services =>
    services.ReplaceDbProvider<AppDbContext>(options =>
        options.UseSqlite(connection)));

// Control time in tests
_factory = _factory.WithFakeTimeProvider(out var fakeTime);
fakeTime.SetUtcNow(DateTimeOffset.UtcNow.AddDays(-7));
```

## Key Features
- **WebApplicationFactory helpers** — `CreateClientWithActor` injects test actors via HTTP headers
- **DI service replacement** — `ReplaceDbProvider`, `ReplaceSingleton`, `ReplaceResourceLoader`
- **Fake time provider** — `WithFakeTimeProvider` for controlling `TimeProvider` in tests
- **MSAL token acquisition** — `MsalTestTokenProvider` for E2E tests against real Entra ID tenants
- **`.http` replay** — parse service examples, chain named responses, generate a fresh `{{$guid}}` per occurrence, and reject unresolved placeholders before sending

Use `HttpFileParser.ParseFile`, `HttpFileRunner.RunAsync`, and
`HttpFileAssertions.AssertExpectationsMet` from `Trellis.Testing.AspNetCore.Http` with a
`WebApplicationFactory` client to guard service `.http` examples in CI. GUIDs refresh even
when the same parsed requests are reused; missing variables fail with request/token/location
diagnostics. The caller disposes successful responses. See the API reference for supported
syntax and the narrower Showcase live-host transcript script.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-testing.html)
- [Package API reference](../docs/docfx_project/api_reference/trellis-api-testing-aspnetcore.md)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
Requires [Trellis.Testing](https://www.nuget.org/packages/Trellis.Testing) for assertions and test doubles.

## Development

Run the package tests from the repository root:

```powershell
dotnet test Trellis.Testing.AspNetCore\tests\Trellis.Testing.AspNetCore.Tests.csproj -c Release
```

Repository tests require PowerShell 7 (`pwsh`) to exercise the real Showcase replay script,
including a loopback host. Consumers of the .NET replay helpers do not need PowerShell.
