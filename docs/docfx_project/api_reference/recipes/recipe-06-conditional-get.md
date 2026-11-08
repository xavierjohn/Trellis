---
name: recipe-06-conditional-get
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows conditional GET with EntityTagValue. Use when a read should answer 304 Not Modified because the client's If-None-Match still matches."
---
# Recipe 6 — Conditional GET with `EntityTagValue`

**Requires:** `Trellis.Core`, plus `Trellis.Asp`, `Trellis.Http.Abstractions`.

**Problem.** Serve a resource with strong-ETag conditional GET so clients can revalidate cheaply.

```csharp
using Trellis;
using Trellis.Asp;

app.MapGet("/blobs/{id:guid}", async (Guid id, IBlobRepository repo, CancellationToken ct) =>
{
    Result<BlobContent> result = await repo.FindAsync(new BlobId(id), ct);

    return result.ToHttpResponse(opts => opts
        .WithETag(b => EntityTagValue.Strong(b.Sha256Hex))
        .WithLastModified(b => b.UploadedAt)
        .EvaluatePreconditions());
});
```

**What it shows.** `EntityTagValue.Strong(...)` and `EntityTagValue.Weak(...)` build typed ETags; `WithETag` accepts either a `string` (always strong) or an `EntityTagValue`. On `GET` and `HEAD` only, `.EvaluatePreconditions()` evaluates `If-Match` (strong comparison), else `If-Unmodified-Since`; then `If-None-Match` (weak comparison), else `If-Modified-Since`, against the configured ETag and `Last-Modified` selectors, and answers `412 Precondition Failed` or `304 Not Modified`. It does nothing on `PUT`, `PATCH` or `DELETE`, so guard those before mutating (see [Recipe 23](recipe-23-if-match-concurrency.md)). A `MapGet` endpoint does not answer `HEAD`; map it with `MapMethods` to serve both. For byte-range responses, call ASP.NET Core's `Results.File(stream, enableRangeProcessing: true)` directly — Trellis does not wrap that surface.
