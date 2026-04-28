# Trellis.Core.Generator (bundled inside Trellis.Core)

> [!IMPORTANT]
> **`Trellis.Core.Generator` is not published as a standalone NuGet package**. It is bundled inside `Trellis.Core.nupkg` under `analyzers/dotnet/cs/` and is attached automatically when you reference `Trellis.Core` (or any package depending on it, e.g. `Trellis.Primitives`).
>
> Remove any `<PackageReference Include="Trellis.Core.Generator" />` from your projects — the package no longer exists on nuget.org.

Source generation for custom scalar value objects built on `RequiredString<TSelf>`, `RequiredGuid<TSelf>`, and related Trellis primitives.

## Installation

No separate install. Adding `Trellis.Core` (or `Trellis.Primitives`) attaches the generator automatically.

```bash
dotnet add package Trellis.Core
# or
dotnet add package Trellis.Primitives
```

## Quick Example
```csharp
using Trellis;

[StringLength(100)]
public partial class CustomerName : RequiredString<CustomerName> { }

var name = CustomerName.Create("Ada");
```

## Key Features
- Generates `Create`, `TryCreate`, parsing, and conversion boilerplate for custom primitives.
- Reads Trellis validation attributes such as `[StringLength]` and `[Range]` at compile time.
- Keeps custom value objects terse without giving up strong typing.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/primitives.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
