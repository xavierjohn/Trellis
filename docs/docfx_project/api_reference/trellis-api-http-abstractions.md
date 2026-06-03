---
package: Trellis.Http.Abstractions
namespaces: [Trellis]
types: [HttpError, AuthChallenge, EntityTagValue, RetryAfterValue, PreconditionKind, RepresentationMetadata, "WriteOutcome<T>", AggregateETagExtensions]
version: v3
last_verified: 2026-06-03
audience: [llm]
---
# Trellis.Http.Abstractions &mdash; API Reference

**Package:** `Trellis.Http.Abstractions`
**Namespace:** `Trellis`
**Purpose:** Shared HTTP transport abstractions used by `Trellis.Http` and `Trellis.Asp` without pulling HTTP-specific payload types into `Trellis.Core`.

See also: [trellis-api-core.md](trellis-api-core.md#moved-http-transport-types), [trellis-api-http.md](trellis-api-http.md#use-this-file-when), [trellis-api-asp.md](trellis-api-asp.md#domain--http-boundary-mapping).

## Use this file when

- You need the HTTP-specific fault cases wrapped by `Error.TransportFault`.
- You need typed header / validator helpers such as `EntityTagValue`, `RetryAfterValue`, or `PreconditionKind`.
- You need response metadata or write-outcome shapes (`RepresentationMetadata`, `WriteOutcome<T>`).
- You are applying aggregate ETag preconditions from application code.
- All types in this package live in the `Trellis` namespace. After adding the `Trellis.Http.Abstractions` package reference, the existing `using Trellis;` (or implicit usings) brings them into scope; no new `using` directive is required.

## Package role

- `Trellis.Core` keeps the transport-neutral envelope: `ITransportFault`, `RetryAdvice`, and `Error.TransportFault`.
- `Trellis.Http.Abstractions` supplies the built-in HTTP payload union (`HttpError`) plus the HTTP value objects and response-shape helpers that would otherwise drag HTTP-specific concerns into Core.
- The CLR namespace stays `Trellis`, so most consumer code changes are package-reference updates rather than `using` changes.

## `HttpError`

`HttpError` is a closed HTTP-fault union that implements `ITransportFault`.
Construct it only in HTTP-aware boundaries and wrap it in `new Error.TransportFault(...)` when you need to move it through a `Result` pipeline.

| Case | Constructor | Typical status | Notes |
| --- | --- | --- | --- |
| `HttpError.MethodNotAllowed` | `(EquatableArray<string> Allow)` | 405 | Preserves the `Allow` header payload. |
| `HttpError.NotAcceptable` | `(EquatableArray<string> Available)` | 406 | Available representations/media types. |
| `HttpError.UnsupportedMediaType` | `(EquatableArray<string> Supported)` | 415 | Supported request media types. |
| `HttpError.RangeNotSatisfiable` | `(long CompleteLength, string Unit = "bytes")` | 416 | Preserves the unsatisfied-range payload (complete length and unit) for HTTP-aware boundaries. |
| `HttpError.ContentTooLarge` | `(long? MaxBytes = null)` | 413 | Optional request-size limit payload. |
| `HttpError.PreconditionFailed` | `(ResourceRef Resource, PreconditionKind Condition)` | 412 | Typed conditional-request failure. |
| `HttpError.PreconditionRequired` | `(PreconditionKind Condition)` | 428 | Missing required precondition. |

### Base members

| Member | Type | Notes |
| --- | --- | --- |
| `Kind` | `string` | Stable HTTP-aligned discriminator (for example `"method-not-allowed"`). |
| `Code` | `string` | Defaults to `Kind`; precondition cases override it with the specific `PreconditionKind`. |
| `Detail` | `string?` | Optional human-readable detail. |
| `Cause` | `HttpError?` | Optional structured cause chain; cycles throw `InvalidOperationException`. |

## Header and conditional-request value types

| Type | Shape | Notes |
| --- | --- | --- |
| `AuthChallenge` | `sealed record (string Scheme, ImmutableDictionary<string,string>? Params = null)` | Standalone `WWW-Authenticate` challenge model. It is not stored on `Error.AuthenticationRequired`; HTTP-aware callers can still use it to construct headers directly. |
| `EntityTagValue` | `sealed record` | Strong / weak / wildcard ETag value with `Strong`, `Weak`, `Wildcard`, `TryParse`, `StrongEquals`, `WeakEquals`, and `ToHeaderValue()`. |
| `RetryAfterValue` | `sealed class` | `Retry-After` as either delay seconds or an absolute date via `FromSeconds`, `FromDate`, and `ToHeaderValue()`. |
| `PreconditionKind` | `enum { IfMatch, IfNoneMatch, IfModifiedSince, IfUnmodifiedSince }` | Typed vocabulary for conditional headers. |

## Representation metadata and write outcomes

| Type | Purpose |
| --- | --- |
| `RepresentationMetadata` | Response metadata bag for `ETag`, `Last-Modified`, `Vary`, `Content-Language`, and `Content-Location`. Build with `RepresentationMetadata.Create()` or the convenience helpers `WithETag(...)` / `WithStrongETag(...)`. |
| `WriteOutcome<T>` | Closed union for HTTP-shaped write results: `Created`, `Updated`, `UpdatedNoContent`, `Accepted`, and `AcceptedNoContent`. The `Accepted*` cases can still carry `RetryAfterValue`. |

## `AggregateETagExtensions`

`AggregateETagExtensions` now lives in this package alongside the ETag types it depends on.
The public signatures stay the same (`OptionalETag*` / `RequireETag*` over `Result<T>` / `Task<Result<T>>` / `ValueTask<Result<T>>`), but failures now flow as transport faults:

- missing `If-Match` on `RequireETag*` → `Error.TransportFault(new HttpError.PreconditionRequired(PreconditionKind.IfMatch))`
- empty / weak-only / non-matching ETag sets → `Error.TransportFault(new HttpError.PreconditionFailed(ResourceRef.For<T>(), PreconditionKind.IfMatch))`

## Domain ↔ transport boundary

`Error.AuthenticationRequired`, `Error.RateLimited`, and `Error.Unavailable` live in `Trellis.Core` as transport-neutral cases. They carry transport-neutral payloads (`Scheme`, `RetryAdvice`) that the ASP boundary translates to HTTP headers (`WWW-Authenticate`, `Retry-After`). This package supplies the 405/406/412/413/415/416/428 payloads via `HttpError`.