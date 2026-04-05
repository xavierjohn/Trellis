# EF Core Example

This example shows how Trellis value objects and result-based workflows stay clean when persisted with Entity Framework Core.

## What You'll Learn
- How `ApplyTrellisConventions` removes most value-converter boilerplate
- How validated value objects work for IDs, names, email addresses, and money
- How an order state machine can be created, queried, and saved through EF Core

## Prerequisites
- .NET 10 SDK

## Run It
```bash
dotnet run
```

## Key Files
| File | What It Shows |
|------|--------------|
| `Program.cs` | End-to-end console demo with products, customers, and orders |
| `Data/AppDbContext.cs` | EF Core conventions and model configuration |
| `Entities/Customer.cs` | Customer creation with validated primitives |
| `Entities/Product.cs` | Product creation and inventory-safe fields |
| `Entities/Order.cs` | Order lifecycle and totals |
| `Enums/OrderState.cs` | State behavior used by the order aggregate |

## Related Docs
- [Entity Framework Core Integration](https://xavierjohn.github.io/Trellis/articles/integration-ef.html)
- [Primitive Value Objects](https://xavierjohn.github.io/Trellis/articles/primitives.html)
- [State Machines](https://xavierjohn.github.io/Trellis/articles/state-machines.html)
