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

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-testing.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
