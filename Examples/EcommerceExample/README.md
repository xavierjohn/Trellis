# Ecommerce Example

This example walks through an order-processing workflow with inventory reservation, payment handling, notifications, recovery, and domain events.

## What You'll Learn
- How an order aggregate controls status changes and line items
- How to orchestrate async business steps with `BindAsync`, `CheckAsync`, and recovery
- How specifications make filtering and business rules reusable

## Prerequisites
- .NET 10 SDK

## Run It
```bash
dotnet run
```

## Scenarios Included
- Simple order creation
- Full order workflow
- Payment failure with recovery
- Insufficient inventory handling
- Domain event and specification examples

## Key Files
| File | What It Shows |
|------|--------------|
| `Program.cs` | Console entry point |
| `EcommerceExamples.cs` | The runnable example set |
| `Aggregates/Order.cs` | Order lifecycle, totals, and transitions |
| `Workflows/OrderWorkflow.cs` | End-to-end workflow composition |
| `Services/PaymentService.cs` | Payment processing and retry-oriented failures |
| `Services/InventoryService.cs` | Reservation and release of stock |
| `Specifications/OrderSpecifications.cs` | Reusable order filters |

## Related Docs
- [Examples](https://xavierjohn.github.io/Trellis/articles/examples.html)
- [Specifications](https://xavierjohn.github.io/Trellis/articles/specifications.html)
- [Advanced Features](https://xavierjohn.github.io/Trellis/articles/advanced-features.html)
