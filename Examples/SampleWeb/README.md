# SampleWeb

Two related projects that share a single domain library and demonstrate how Trellis
plugs into a Minimal API host.

| Project | Role |
|---|---|
| `SampleUserLibrary/` | Pure-domain class library — value objects, aggregates, services. No ASP.NET, no EF Core, no FluentValidation. Reusable across hosts. |
| `SampleMinimalApi/` | Minimal API host that consumes `SampleUserLibrary` and exposes user/product/order endpoints. |

The MVC equivalent of this story is the [`Showcase`](../Showcase/) sample. SampleWeb
exists to show the **Minimal API** flavor of the same patterns.

## Run it

```pwsh
dotnet run --project SampleMinimalApi/SampleMinimalApi.csproj
```

## Why two projects?

`SampleUserLibrary` is intentionally a class library — it is the **canonical place
to look for how a Trellis aggregate should be written** (pure ROP `TryCreate`, no
FluentValidation, no framework refs). `SampleMinimalApi` is the host that wires
that domain to HTTP. Splitting them keeps axiom A8 (domain purity) honest and shows
the same domain library could back any number of hosts.

## Related Docs

- [ASP.NET Core Integration](https://xavierjohn.github.io/Trellis/articles/integration-aspnet.html)
- [Examples README](../README.md) — the 11-axiom contract every sample is held to.
