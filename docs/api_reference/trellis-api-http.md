# Trellis.Http — API Reference

> Part of the [Trellis API Reference](.). See also: trellis-api-results.md, trellis-api-asp.md.

**Package:** `Trellis.Http` | **Namespace:** `Trellis.Http`

Fluent pipeline for `HttpResponseMessage` → `Result<T>`:

```csharp
// Status handlers (chainable, each returns Result<HttpResponseMessage>)
HandleNotFound(this HttpResponseMessage, NotFoundError)
HandleUnauthorized(this HttpResponseMessage, UnauthorizedError)
HandleForbidden(this HttpResponseMessage, ForbiddenError)
HandleConflict(this HttpResponseMessage, ConflictError)
HandleClientError(this HttpResponseMessage, Func<HttpStatusCode, Error>)
HandleServerError(this HttpResponseMessage, Func<HttpStatusCode, Error>)
EnsureSuccess(this HttpResponseMessage, Func<HttpStatusCode, Error>? errorFactory = null)

// Custom async error handling with context
Task<Result<HttpResponseMessage>> HandleFailureAsync<TContext>(this HttpResponseMessage,
    Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callback, TContext context, CancellationToken cancellationToken)
Task<Result<HttpResponseMessage>> HandleFailureAsync<TContext>(this Task<HttpResponseMessage>,
    Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callback, TContext context, CancellationToken cancellationToken)

// Also chainable on Result<HttpResponseMessage> for fluent error handling
HandleNotFound(this Result<HttpResponseMessage>, NotFoundError)
// ... etc.

// JSON deserialization
Task<Result<T>> ReadResultFromJsonAsync<T>(this HttpResponseMessage, JsonTypeInfo<T>, CancellationToken)
Task<Result<Maybe<T>>> ReadResultMaybeFromJsonAsync<T>(this HttpResponseMessage, JsonTypeInfo<T>, CancellationToken)
// + overloads on Task<HttpResponseMessage>, Result<HttpResponseMessage>, Task<Result<HttpResponseMessage>>
```

### Usage Pattern

```csharp
var result = await httpClient.GetAsync($"/api/orders/{id}")
    .HandleNotFoundAsync(Error.NotFound($"Order {id} not found"))
    .HandleUnauthorizedAsync(Error.Unauthorized("Not authenticated"))
    .EnsureSuccessAsync()
    .ReadResultFromJsonAsync(OrderJsonContext.Default.Order, ct);
```

---
