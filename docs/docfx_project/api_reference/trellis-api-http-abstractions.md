---
package: Trellis.Http.Abstractions
namespaces: [Trellis]
types: [HttpError, AuthChallenge, EntityTagValue, RetryAfterValue, PreconditionKind, RepresentationMetadata, "RepresentationMetadata.Builder", "WriteOutcome<T>", WriteOutcome, AggregateETagExtensions]
version: v3
last_verified: 2026-06-19
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

### `EntityTagValue` members

| Member | Type | Notes |
| --- | --- | --- |
| `OpaqueTag` | `string` | The raw tag with no quotes and no `W/` prefix. Compare against `IAggregate.ETag` with `StringComparison.Ordinal`. |
| `IsWeak` | `bool` | `true` for `W/"..."`. Weak tags do **not** satisfy `If-Match`, which requires strong comparison (RFC 9110 §13.1.1). |
| `IsWildcard` | `bool` | `true` only for the RFC 9110 `*` token, as opposed to a literal ETag whose opaque tag happens to be `*`. A wildcard satisfies `If-Match` unconditionally. |
| `Strong(string)` / `Weak(string)` / `Wildcard` | factories | Construction. `Strong`/`Weak` validate the opaque-tag character set and throw on invalid input. |
| `TryParse(string)` | parser | Parses a header value, including the `W/` prefix. |
| `StrongEquals` / `WeakEquals` | comparison | RFC 9110 §8.8.3.2 comparison functions. |
| `ToHeaderValue()` | `string` | Formats for the wire, re-adding quotes and any `W/` prefix. |

### `RetryAfterValue` members

`Retry-After` is a union of two shapes, so **test the discriminant before reading the payload** — the accessors throw `InvalidOperationException` for the wrong case.

| Member | Type | Notes |
| --- | --- | --- |
| `IsDelaySeconds` | `bool` | `true` when the value is a delay. |
| `IsDate` | `bool` | `true` when the value is an absolute HTTP-date. |
| `DelaySeconds` | `int` | Throws `InvalidOperationException` when `IsDate`. |
| `Date` | `DateTimeOffset` | Throws `InvalidOperationException` when `IsDelaySeconds`. |
| `FromSeconds(int)` | factory | Throws `ArgumentOutOfRangeException` when negative. |
| `FromDate(DateTimeOffset)` | factory | Absolute-date form. |
| `ToHeaderValue()` | `string` | Decimal seconds, or an IMF-fixdate (`"R"`) for the date form. |

## Representation metadata and write outcomes

| Type | Purpose |
| --- | --- |
| `RepresentationMetadata` | Response metadata bag for `ETag`, `Last-Modified`, `Vary`, `Content-Language`, and `Content-Location`. Build with `RepresentationMetadata.Create()` or the convenience helpers `WithETag(...)` / `WithStrongETag(...)`. |
| `WriteOutcome<T>` | Closed union for HTTP-shaped write results: `Created`, `Updated`, `UpdatedNoContent`, `Accepted`, and `AcceptedNoContent`. The `Accepted*` cases can still carry `RetryAfterValue`. Construct via the nested records (`new WriteOutcome<T>.Updated(...)`) or — to recover the base type without a cast — the static `WriteOutcome` factory helpers below. |
| `WriteOutcome` (static) | Factory helpers — `Created<T>`, `Updated<T>`, `UpdatedNoContent<T>`, `Accepted<T>`, `AcceptedNoContent<T>` — that build each case but **return the base `WriteOutcome<T>`**. This lets results flow through generic pipelines such as `Result.Map(...)` / `ToHttpResponse(...)` (which bind on `Result<WriteOutcome<T>>`) **without an explicit `(WriteOutcome<T>)` cast**: `new WriteOutcome<T>.Updated(...)` has the nested case type, and `Result<T>` invariance then blocks the implicit upcast. Mirrors the non-generic `Result` / generic `Result<T>` pairing. `T` is inferred from the value for `Created`/`Updated`/`Accepted`; specify it explicitly for the no-content cases. |

### `RepresentationMetadata` members

| Member | Type | Notes |
| --- | --- | --- |
| `ETag` | `EntityTagValue?` | Validator for the selected representation. Passing a wildcard throws `ArgumentException` — a wildcard is a request-side token and is never a valid response validator. |
| `LastModified` | `DateTimeOffset?` | Emitted as `Last-Modified`. |
| `Vary` | `IReadOnlyList<string>?` | Request fields that influenced representation selection. |
| `ContentLanguage` | `IReadOnlyList<string>?` | Emitted as `Content-Language`. |
| `ContentLocation` | `string?` | Emitted as `Content-Location`. |
| `Create()` | `RepresentationMetadata.Builder` | Starts the fluent builder. |
| `WithETag(EntityTagValue)` / `WithStrongETag(string)` | factories | Shortcuts when the ETag is the only metadata. |

### `RepresentationMetadata.Builder`

Fluent builder returned by `RepresentationMetadata.Create()`; every setter returns the builder, and `Build()` produces the immutable instance.

| Method | Notes |
| --- | --- |
| `SetETag(EntityTagValue)` | Sets the validator directly. |
| `SetStrongETag(string)` / `SetWeakETag(string)` | Sets the validator from an opaque tag. |
| `SetLastModified(DateTimeOffset)` | Sets `Last-Modified`. |
| `AddVary(params string[])` | Appends `Vary` field names, deduplicating case-insensitively. |
| `AddContentLanguage(params string[])` | Appends language tags, deduplicating case-insensitively. |
| `SetContentLocation(string)` | Sets `Content-Location`. |
| `Build()` | Produces the `RepresentationMetadata`. Empty `Vary` / `ContentLanguage` collections are normalised to `null`. |

### `WriteOutcome<T>` case payloads

| Case | Parameters | Transports as |
| --- | --- | --- |
| `Created` | `T Value`, `string Location`, `RepresentationMetadata? Metadata = null` | `201 Created` |
| `Updated` | `T Value`, `RepresentationMetadata? Metadata = null` | `200 OK` |
| `UpdatedNoContent` | `RepresentationMetadata? Metadata = null` | `204 No Content` |
| `Accepted` | `T StatusBody`, `string? MonitorUri = null`, `RetryAfterValue? RetryAfter = null` | `202 Accepted` |
| `AcceptedNoContent` | `string? MonitorUri = null`, `RetryAfterValue? RetryAfter = null` | `202 Accepted` |

`StatusBody` describes the in-flight operation; `MonitorUri` is the address a client polls for progress; `RetryAfter` hints when to poll next.

## `AggregateETagExtensions`

`AggregateETagExtensions` now lives in this package alongside the ETag types it depends on.
Failures flow as transport faults:

- missing `If-Match` on `RequireETag*` → `Error.TransportFault(new HttpError.PreconditionRequired(PreconditionKind.IfMatch))`
- empty / weak-only / non-matching ETag sets → `Error.TransportFault(new HttpError.PreconditionFailed(ResourceRef.For<T>(), PreconditionKind.IfMatch))`

All overloads are constrained to `T : IAggregate` and take `EntityTagValue[]?` — normally the parsed `If-Match` header.

| Method | Receiver | Behaviour when the header is absent (`null`) |
| --- | --- | --- |
| `OptionalETag<T>` | `Result<T>` | Proceeds unconditionally. |
| `OptionalETagAsync<T>` | `Task<Result<T>>`, `ValueTask<Result<T>>` | As above; async overloads of `OptionalETag`. |
| `RequireETag<T>` | `Result<T>` | Fails with `HttpError.PreconditionRequired` (HTTP 428). |
| `RequireETagAsync<T>` | `Task<Result<T>>`, `ValueTask<Result<T>>` | As above; async overloads of `RequireETag`. |

Choosing between them is a service-owner decision: use `RequireETag*` on endpoints where a lost update is unacceptable, `OptionalETag*` where conditional requests are a client optimisation. Matching is strong comparison, so a wildcard `*` always passes and weak tags never do.

## Domain ↔ transport boundary

`Error.AuthenticationRequired`, `Error.RateLimited`, and `Error.Unavailable` live in `Trellis.Core` as transport-neutral cases. They carry transport-neutral payloads (`Scheme`, `RetryAdvice`) that the ASP boundary translates to HTTP headers (`WWW-Authenticate`, `Retry-After`). This package supplies the 405/406/412/413/415/416/428 payloads via `HttpError`.