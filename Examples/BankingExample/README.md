# Banking Example

This example models a banking domain with transfers, fraud checks, daily limits, recovery behavior, and domain events.

## What You'll Learn
- How to keep account rules inside an aggregate
- How to compose fraud checks, MFA-style decisions, and transfers with result pipelines
- How recovery and domain events support safer financial workflows

## Prerequisites
- .NET 10 SDK

## Run It
```bash
dotnet run
```

## Scenarios Included
- Basic account operations
- Transfers between accounts
- Fraud detection and account freezing
- Daily withdrawal limits
- Interest payments and change tracking

## Key Files
| File | What It Shows |
|------|--------------|
| `Program.cs` | Console entry point |
| `BankingExamples.cs` | The runnable scenario set |
| `Aggregates/BankAccount.cs` | Account rules, balances, limits, and events |
| `Services/FraudDetectionService.cs` | Fraud scoring, suspicious-pattern checks, and MFA decisions |
| `Workflows/BankingWorkflow.cs` | Transfer and withdrawal orchestration |
| `Entities/Transaction.cs` | The transaction records used for balances and audit history |

## Related Docs
- [Advanced Features](https://xavierjohn.github.io/Trellis/articles/advanced-features.html)
- [State Machines](https://xavierjohn.github.io/Trellis/articles/state-machines.html)
- [Examples](https://xavierjohn.github.io/Trellis/articles/examples.html)
