---
name: recipe-30-rehydration
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Contrasts fail-loud and Result-track rehydration of entities from persistence. Use when materializing an entity from a database row."
---
# Recipe 30 — Rehydrating entities from persistence: fail-loud vs Result-track

## Contents

- [Pattern A — fail-loud rehydration (the 90% case)](#pattern-a--fail-loud-rehydration-the-90-case)
- [Pattern B — Result-track end-to-end (the legacy-data case)](#pattern-b--result-track-end-to-end-the-legacy-data-case)
- [Choosing between A and B](#choosing-between-a-and-b)

**Problem.** A repository loads a row from the database and needs to reconstruct a domain entity whose value-object fields each have `TryCreate(...) → Result<TVO>`. Recipe 18 covers the inbound direction (request DTO → command, with `Result.Combine + Map`). The inverse direction — DB row → entity — has a different failure semantic:

- **Inbound** is *untrusted* input — every `TryCreate` failure is a legitimate validation response and must surface as `Error.InvalidInput` on the Result track so the caller can fix the request.
- **Outbound** is *trusted* input — the row was written through the same `TryCreate` chain (write-path validation). A `TryCreate` failure on read means the row predates the validation rule (legacy data), the rule changed since (migration drift), or the database was tampered with — none of which the *application caller* can fix.

Picking the right rehydration shape depends on which trust model applies to that specific aggregate.

## Pattern A — fail-loud rehydration (the 90% case)

When write-path validation is guaranteed (`TryCreate` enforced at every insert + update + migration backfill), a `TryCreate` failure on read is an operator bug. Use `Result<T>.GetValueOrThrow(string? errorMessage)` so the failure is loud, immediate, and names the offending row:

```csharp
using Trellis;

public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public async Task<Result<User>> FindByIdAsync(UserId id, CancellationToken ct)
    {
        var row = await db.UserRows.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        if (row is null)
            return Result.Fail<User>(new Error.NotFound(ResourceRef.For<User>(id)));

        // Write-path TryCreate guarantees these are valid. A failure here is database
        // corruption / migration drift; throw to fail loud rather than surface a
        // "validation" failure to the application layer (which cannot act on it).
        var user = User.TryCreate(
            UserId.TryCreate(row.Id).GetValueOrThrow($"Corrupt User.Id in row {row.Id}"),
            FirstName.TryCreate(row.FirstName).GetValueOrThrow($"Corrupt User.FirstName in row {row.Id}"),
            LastName.TryCreate(row.LastName).GetValueOrThrow($"Corrupt User.LastName in row {row.Id}"),
            EmailAddress.TryCreate(row.Email).GetValueOrThrow($"Corrupt User.Email in row {row.Id}"))
            .GetValueOrThrow($"Corrupt User aggregate for row {row.Id}");

        return Result.Ok(user);
    }
}
```

`GetValueOrThrow` throws `InvalidOperationException` whose message names the offending row. The exception bubbles through `ExceptionBehavior` and surfaces as `new Error.Unexpected("unhandled-exception", faultId)` to the wire (HTTP 500), with the full message in operator-side logs — exactly the shape for "this should never have happened."

**Why not `Trellis.Testing.Unwrap()`?** `Unwrap()` is a test-only helper (see [Recipe 18](recipe-18-dto-to-value-object.md)). Production code that uses it mixes test and production seams and is harder to grep for than a named-verb extractor. `GetValueOrThrow` ships in `Trellis.Core` and the verb in the name makes the failure mode explicit at every call site.

## Pattern B — Result-track end-to-end (the legacy-data case)

When write-path validation cannot be guaranteed — the table predates the current rules, an external migration imports rows from a third-party system, or the column may contain genuinely corruptible data — keep the failure on the Result track and let the application layer decide:

```csharp
public sealed class LegacyContactRepository(AppDbContext db) : ILegacyContactRepository
{
    public Task<Result<Contact>> FindByIdAsync(ContactId id, CancellationToken ct) =>
        db.ContactRows.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct)
            .EnsureNotNullAsync(() => new Error.NotFound(ResourceRef.For<Contact>(id)))
            .BindAsync(row => Result.Combine(
                ContactId.TryCreate(row.Id, "Id"),
                FirstName.TryCreate(row.FirstName, "FirstName"),
                EmailAddress.TryCreate(row.Email, "Email"))
                .Bind((cid, firstName, email) => Contact.TryCreate(cid, firstName, email)));
}
```

The application layer then matches on the typed failure:

```csharp
var result = await repo.FindByIdAsync(id, ct);
return result.Match(
    onSuccess: contact => Ok(ContactResponse.From(contact)),
    onFailure: err => err switch
    {
        Error.NotFound       => NotFound(),
        Error.InvalidInput i => UnprocessableEntity(i.Fields), // row is invalid — surface why
        _                    => Problem(),
    });
```

## Choosing between A and B

| Indicator | Pattern A (fail-loud) | Pattern B (Result-track) |
|---|---|---|
| Write path goes through `TryCreate`? | Yes | No (raw insert / external import / pre-validation legacy table) |
| Schema migrations preserve invariants? | Yes (every column-add ships a backfill that satisfies `TryCreate`) | No (some columns may have legacy values) |
| Who fixes a failure? | Operator (it's a bug — read the log, fix the data, redeploy) | Application / user (the data is genuinely invalid; surface and let them correct it) |
| Wire shape on failure | HTTP 500 (`Error.Unexpected`) | HTTP 422 (`Error.InvalidInput`) or domain-specific |

If you don't have one clear answer for a given aggregate, default to **Pattern A** — it's the higher-leverage shape because the failure is operator-actionable, and it matches the write-path invariants you're already enforcing at the API seam.

**Anti-pattern → fix.**

```csharp
// ❌ Wrong — Trellis.Testing.Unwrap() in production code. Mixes test and production seams;
//   harder to grep for than a named-verb extraction; the test-only contract is documented
//   at Recipe 18.
var user = User.TryCreate(
    UserId.TryCreate(row.Id).Unwrap(),
    EmailAddress.TryCreate(row.Email).Unwrap()).Unwrap();
return Result.Ok(user);

// ❌ Wrong — inline .Match(v => v, e => throw …). 50+ characters of ceremony at every call
//   site; obscures intent; every codebase reinvents its own exception type and message format.
var user = User.TryCreate(
    UserId.TryCreate(row.Id).Match(v => v, e => throw new InvalidOperationException(e.ToString())),
    EmailAddress.TryCreate(row.Email).Match(v => v, e => throw new InvalidOperationException(e.ToString())))
    .Match(v => v, e => throw new InvalidOperationException(e.ToString()));
return Result.Ok(user);

// ✅ Correct — GetValueOrThrow with a row-identifying message at each seam.
var user = User.TryCreate(
    UserId.TryCreate(row.Id).GetValueOrThrow($"Corrupt User.Id in row {row.Id}"),
    EmailAddress.TryCreate(row.Email).GetValueOrThrow($"Corrupt User.Email in row {row.Id}"))
    .GetValueOrThrow($"Corrupt User aggregate for row {row.Id}");
return Result.Ok(user);
```
