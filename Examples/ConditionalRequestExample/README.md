# Conditional Request Example (RFC 9110)

Demonstrates **ETag-based optimistic concurrency** using Trellis with a Minimal API and SQLite.

This example covers the full [RFC 9110](https://www.rfc-editor.org/rfc/rfc9110) conditional-request flow:

| Header | Verb | Behaviour |
|---|---|---|
| `ETag` | GET / POST / PUT | Returned on every successful response |
| `If-None-Match` | GET | Returns **304 Not Modified** when the ETag matches |
| `If-Match` | PUT | Returns **412 Precondition Failed** when the ETag is stale |

## Key Trellis APIs used

- `Aggregate<TId>.ETag` — built-in concurrency token
- `AggregateETagConvention` + `AggregateETagInterceptor` — automatic ETag management via `ApplyTrellisConventions` / `AddTrellisInterceptors`
- `OptionalETag(ifMatchETag)` — validates the `If-Match` precondition
- `ToHttpResult(httpContext, etagSelector, map)` — sets the `ETag` header and handles `If-None-Match → 304`

## Run

```bash
dotnet run --project Examples/ConditionalRequestExample
```

The server starts on `http://localhost:5000` by default (or whichever port Kestrel selects).

## Try it with curl

### 1. Create a product

```bash
curl -s -D - -X POST http://localhost:5000/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Widget","price":9.99}'
```

Response includes `ETag: "<value>"` and a `201 Created` body.

### 2. GET — observe the ETag header

```bash
curl -s -D - http://localhost:5000/products/{id}
```

### 3. GET with If-None-Match — 304 Not Modified

```bash
curl -s -D - http://localhost:5000/products/{id} \
  -H 'If-None-Match: "<etag>"'
```

Returns **304** with an empty body when the ETag matches.

### 4. PUT with If-Match — conditional update

```bash
curl -s -D - -X PUT http://localhost:5000/products/{id} \
  -H "Content-Type: application/json" \
  -H 'If-Match: "<etag>"' \
  -d '{"price":12.99}'
```

Succeeds with **200 OK** and a **new** ETag.

### 5. PUT with stale If-Match — 412 Precondition Failed

Replay the same PUT (the old ETag is now stale):

```bash
curl -s -D - -X PUT http://localhost:5000/products/{id} \
  -H "Content-Type: application/json" \
  -H 'If-Match: "<old-etag>"' \
  -d '{"price":14.99}'
```

Returns **412 Precondition Failed** because the resource has been modified.

### 6. PUT without If-Match — unconditional update

```bash
curl -s -D - -X PUT http://localhost:5000/products/{id} \
  -H "Content-Type: application/json" \
  -d '{"price":14.99}'
```

When no `If-Match` header is sent, `OptionalETag` is a no-op and the update proceeds unconditionally.
