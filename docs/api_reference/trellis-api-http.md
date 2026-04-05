# Trellis.Http — API Reference

**Package:** `Trellis.Http`  
**Namespace:** `Trellis.Http`  
**Purpose:** Fluent `HttpResponseMessage` / `Result<HttpResponseMessage>` extensions for status handling and JSON deserialization into `Result<T>` and `Result<Maybe<T>>`.

## Types

### `HttpResponseExtensions`

**Declaration**

```csharp
public static partial class HttpResponseExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<HttpResponseMessage> HandleNotFound(this HttpResponseMessage response, NotFoundError notFoundError)` | `Result<HttpResponseMessage>` | Returns `Failure(notFoundError)` only when `response.StatusCode == HttpStatusCode.NotFound`; otherwise returns `Success(response)`. |
| `public static async Task<Result<HttpResponseMessage>> HandleNotFoundAsync(this Task<HttpResponseMessage> responseTask, NotFoundError notFoundError)` | `Task<Result<HttpResponseMessage>>` | Awaits `responseTask`, then applies `HandleNotFound(HttpResponseMessage, NotFoundError)`. |
| `public static Result<HttpResponseMessage> HandleUnauthorized(this HttpResponseMessage response, UnauthorizedError unauthorizedError)` | `Result<HttpResponseMessage>` | Returns `Failure(unauthorizedError)` only when `response.StatusCode == HttpStatusCode.Unauthorized`; otherwise returns `Success(response)`. |
| `public static async Task<Result<HttpResponseMessage>> HandleUnauthorizedAsync(this Task<HttpResponseMessage> responseTask, UnauthorizedError unauthorizedError)` | `Task<Result<HttpResponseMessage>>` | Awaits `responseTask`, then applies `HandleUnauthorized(HttpResponseMessage, UnauthorizedError)`. |
| `public static Result<HttpResponseMessage> HandleForbidden(this HttpResponseMessage response, ForbiddenError forbiddenError)` | `Result<HttpResponseMessage>` | Returns `Failure(forbiddenError)` only when `response.StatusCode == HttpStatusCode.Forbidden`; otherwise returns `Success(response)`. |
| `public static async Task<Result<HttpResponseMessage>> HandleForbiddenAsync(this Task<HttpResponseMessage> responseTask, ForbiddenError forbiddenError)` | `Task<Result<HttpResponseMessage>>` | Awaits `responseTask`, then applies `HandleForbidden(HttpResponseMessage, ForbiddenError)`. |
| `public static Result<HttpResponseMessage> HandleConflict(this HttpResponseMessage response, ConflictError conflictError)` | `Result<HttpResponseMessage>` | Returns `Failure(conflictError)` only when `response.StatusCode == HttpStatusCode.Conflict`; otherwise returns `Success(response)`. |
| `public static async Task<Result<HttpResponseMessage>> HandleConflictAsync(this Task<HttpResponseMessage> responseTask, ConflictError conflictError)` | `Task<Result<HttpResponseMessage>>` | Awaits `responseTask`, then applies `HandleConflict(HttpResponseMessage, ConflictError)`. |
| `public static Result<HttpResponseMessage> HandleClientError(this HttpResponseMessage response, Func<HttpStatusCode, Error> errorFactory)` | `Result<HttpResponseMessage>` | Returns `Failure(errorFactory(response.StatusCode))` only for `400 <= status < 500`; otherwise returns `Success(response)`. |
| `public static async Task<Result<HttpResponseMessage>> HandleClientErrorAsync(this Task<HttpResponseMessage> responseTask, Func<HttpStatusCode, Error> errorFactory)` | `Task<Result<HttpResponseMessage>>` | Awaits `responseTask`, then applies `HandleClientError(HttpResponseMessage, Func<HttpStatusCode, Error>)`. |
| `public static Result<HttpResponseMessage> HandleServerError(this HttpResponseMessage response, Func<HttpStatusCode, Error> errorFactory)` | `Result<HttpResponseMessage>` | Returns `Failure(errorFactory(response.StatusCode))` only for `500 <= status < 600`; otherwise returns `Success(response)`. |
| `public static async Task<Result<HttpResponseMessage>> HandleServerErrorAsync(this Task<HttpResponseMessage> responseTask, Func<HttpStatusCode, Error> errorFactory)` | `Task<Result<HttpResponseMessage>>` | Awaits `responseTask`, then applies `HandleServerError(HttpResponseMessage, Func<HttpStatusCode, Error>)`. |
| `public static Result<HttpResponseMessage> EnsureSuccess(this HttpResponseMessage response, Func<HttpStatusCode, Error>? errorFactory = null)` | `Result<HttpResponseMessage>` | Returns `Success(response)` for any successful status code; otherwise returns `Failure(errorFactory(status))` or `Failure(Error.Unexpected(...))` when `errorFactory` is `null`. |
| `public static async Task<Result<HttpResponseMessage>> EnsureSuccessAsync(this Task<HttpResponseMessage> responseTask, Func<HttpStatusCode, Error>? errorFactory = null)` | `Task<Result<HttpResponseMessage>>` | Awaits `responseTask`, then applies `EnsureSuccess(HttpResponseMessage, Func<HttpStatusCode, Error>?)`. |
| `public static async Task<Result<HttpResponseMessage>> HandleFailureAsync<TContext>(this HttpResponseMessage response, Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callbackFailedStatusCode, TContext context, CancellationToken cancellationToken)` | `Task<Result<HttpResponseMessage>>` | Invokes `callbackFailedStatusCode` only when `response.IsSuccessStatusCode == false`; otherwise returns `Success(response)`. |
| `public static async Task<Result<HttpResponseMessage>> HandleFailureAsync<TContext>(this Task<HttpResponseMessage> responseTask, Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callbackFailedStatusCode, TContext context, CancellationToken cancellationToken)` | `Task<Result<HttpResponseMessage>>` | Awaits `responseTask`, then applies `HandleFailureAsync<TContext>(HttpResponseMessage, ...)`. |
| `public static async Task<Result<TValue>> ReadResultFromJsonAsync<TValue>(this HttpResponseMessage response, JsonTypeInfo<TValue> jsonTypeInfo, CancellationToken cancellationToken) where TValue : notnull` | `Task<Result<TValue>>` | Fails on non-success status, `204 NoContent`, `205 ResetContent`, `JsonException`, or `null` JSON value; otherwise returns the deserialized value. |
| `public static async Task<Result<TValue>> ReadResultFromJsonAsync<TValue>(this Task<HttpResponseMessage> responseTask, JsonTypeInfo<TValue> jsonTypeInfo, CancellationToken cancellationToken) where TValue : notnull` | `Task<Result<TValue>>` | Awaits `responseTask`, then applies `ReadResultFromJsonAsync<TValue>(HttpResponseMessage, ...)`. |
| `public static async Task<Result<TValue>> ReadResultFromJsonAsync<TValue>(this Result<HttpResponseMessage> response, JsonTypeInfo<TValue> jsonTypeInfo, CancellationToken cancellationToken) where TValue : notnull` | `Task<Result<TValue>>` | Propagates failure from `response`; on success, deserializes the wrapped `HttpResponseMessage`. |
| `public static async Task<Result<TValue>> ReadResultFromJsonAsync<TValue>(this Task<Result<HttpResponseMessage>> responseTask, JsonTypeInfo<TValue> jsonTypeInfo, CancellationToken cancellationToken) where TValue : notnull` | `Task<Result<TValue>>` | Awaits `responseTask`, then applies `ReadResultFromJsonAsync<TValue>(Result<HttpResponseMessage>, ...)`. |
| `public static async Task<Result<Maybe<TValue>>> ReadResultMaybeFromJsonAsync<TValue>(this HttpResponseMessage response, JsonTypeInfo<TValue> jsonTypeInfo, CancellationToken cancellationToken) where TValue : notnull` | `Task<Result<Maybe<TValue>>>` | Fails on non-success status. Returns `Success(Maybe.None)` for `204`, `205`, `response.Content is null`, empty content, or JSON `null`; otherwise returns `Success(Maybe.From(value))`. `JsonException` is not caught. |
| `public static async Task<Result<Maybe<TValue>>> ReadResultMaybeFromJsonAsync<TValue>(this Task<HttpResponseMessage> responseTask, JsonTypeInfo<TValue> jsonTypeInfo, CancellationToken cancellationToken) where TValue : notnull` | `Task<Result<Maybe<TValue>>>` | Awaits `responseTask`, then applies `ReadResultMaybeFromJsonAsync<TValue>(HttpResponseMessage, ...)`. |
| `public static async Task<Result<Maybe<TValue>>> ReadResultMaybeFromJsonAsync<TValue>(this Result<HttpResponseMessage> response, JsonTypeInfo<TValue> jsonTypeInfo, CancellationToken cancellationToken) where TValue : notnull` | `Task<Result<Maybe<TValue>>>` | Propagates failure from `response`; on success, deserializes the wrapped `HttpResponseMessage` into `Maybe<TValue>`. |
| `public static async Task<Result<Maybe<TValue>>> ReadResultMaybeFromJsonAsync<TValue>(this Task<Result<HttpResponseMessage>> responseTask, JsonTypeInfo<TValue> jsonTypeInfo, CancellationToken cancellationToken) where TValue : notnull` | `Task<Result<Maybe<TValue>>>` | Awaits `responseTask`, then applies `ReadResultMaybeFromJsonAsync<TValue>(Result<HttpResponseMessage>, ...)`. |
| `public static Result<HttpResponseMessage> HandleNotFound(this Result<HttpResponseMessage> result, NotFoundError notFoundError)` | `Result<HttpResponseMessage>` | Applies `HandleNotFound(HttpResponseMessage, NotFoundError)` inside a successful `Result<HttpResponseMessage>`. |
| `public static async Task<Result<HttpResponseMessage>> HandleNotFoundAsync(this Task<Result<HttpResponseMessage>> resultTask, NotFoundError notFoundError)` | `Task<Result<HttpResponseMessage>>` | Awaits `resultTask`, then applies `HandleNotFound(Result<HttpResponseMessage>, NotFoundError)`. |
| `public static Result<HttpResponseMessage> HandleUnauthorized(this Result<HttpResponseMessage> result, UnauthorizedError unauthorizedError)` | `Result<HttpResponseMessage>` | Applies `HandleUnauthorized(HttpResponseMessage, UnauthorizedError)` inside a successful `Result<HttpResponseMessage>`. |
| `public static async Task<Result<HttpResponseMessage>> HandleUnauthorizedAsync(this Task<Result<HttpResponseMessage>> resultTask, UnauthorizedError unauthorizedError)` | `Task<Result<HttpResponseMessage>>` | Awaits `resultTask`, then applies `HandleUnauthorized(Result<HttpResponseMessage>, UnauthorizedError)`. |
| `public static Result<HttpResponseMessage> HandleForbidden(this Result<HttpResponseMessage> result, ForbiddenError forbiddenError)` | `Result<HttpResponseMessage>` | Applies `HandleForbidden(HttpResponseMessage, ForbiddenError)` inside a successful `Result<HttpResponseMessage>`. |
| `public static async Task<Result<HttpResponseMessage>> HandleForbiddenAsync(this Task<Result<HttpResponseMessage>> resultTask, ForbiddenError forbiddenError)` | `Task<Result<HttpResponseMessage>>` | Awaits `resultTask`, then applies `HandleForbidden(Result<HttpResponseMessage>, ForbiddenError)`. |
| `public static Result<HttpResponseMessage> HandleConflict(this Result<HttpResponseMessage> result, ConflictError conflictError)` | `Result<HttpResponseMessage>` | Applies `HandleConflict(HttpResponseMessage, ConflictError)` inside a successful `Result<HttpResponseMessage>`. |
| `public static async Task<Result<HttpResponseMessage>> HandleConflictAsync(this Task<Result<HttpResponseMessage>> resultTask, ConflictError conflictError)` | `Task<Result<HttpResponseMessage>>` | Awaits `resultTask`, then applies `HandleConflict(Result<HttpResponseMessage>, ConflictError)`. |
| `public static Result<HttpResponseMessage> HandleClientError(this Result<HttpResponseMessage> result, Func<HttpStatusCode, Error> errorFactory)` | `Result<HttpResponseMessage>` | Applies `HandleClientError(HttpResponseMessage, Func<HttpStatusCode, Error>)` inside a successful `Result<HttpResponseMessage>`. |
| `public static async Task<Result<HttpResponseMessage>> HandleClientErrorAsync(this Task<Result<HttpResponseMessage>> resultTask, Func<HttpStatusCode, Error> errorFactory)` | `Task<Result<HttpResponseMessage>>` | Awaits `resultTask`, then applies `HandleClientError(Result<HttpResponseMessage>, Func<HttpStatusCode, Error>)`. |
| `public static Result<HttpResponseMessage> HandleServerError(this Result<HttpResponseMessage> result, Func<HttpStatusCode, Error> errorFactory)` | `Result<HttpResponseMessage>` | Applies `HandleServerError(HttpResponseMessage, Func<HttpStatusCode, Error>)` inside a successful `Result<HttpResponseMessage>`. |
| `public static async Task<Result<HttpResponseMessage>> HandleServerErrorAsync(this Task<Result<HttpResponseMessage>> resultTask, Func<HttpStatusCode, Error> errorFactory)` | `Task<Result<HttpResponseMessage>>` | Awaits `resultTask`, then applies `HandleServerError(Result<HttpResponseMessage>, Func<HttpStatusCode, Error>)`. |
| `public static async Task<Result<HttpResponseMessage>> HandleFailureAsync<TContext>(this Result<HttpResponseMessage> result, Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callbackFailedStatusCode, TContext context, CancellationToken cancellationToken)` | `Task<Result<HttpResponseMessage>>` | Propagates failure from `result`; on success, invokes the response-based `HandleFailureAsync<TContext>` overload. |
| `public static async Task<Result<HttpResponseMessage>> HandleFailureAsync<TContext>(this Task<Result<HttpResponseMessage>> resultTask, Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callbackFailedStatusCode, TContext context, CancellationToken cancellationToken)` | `Task<Result<HttpResponseMessage>>` | Awaits `resultTask`, then applies `HandleFailureAsync<TContext>(Result<HttpResponseMessage>, ...)`. |
| `public static Result<HttpResponseMessage> EnsureSuccess(this Result<HttpResponseMessage> result, Func<HttpStatusCode, Error>? errorFactory = null)` | `Result<HttpResponseMessage>` | Propagates failure from `result`; on success, invokes `EnsureSuccess(HttpResponseMessage, Func<HttpStatusCode, Error>?)`. |
| `public static async Task<Result<HttpResponseMessage>> EnsureSuccessAsync(this Task<Result<HttpResponseMessage>> resultTask, Func<HttpStatusCode, Error>? errorFactory = null)` | `Task<Result<HttpResponseMessage>>` | Awaits `resultTask`, then applies `EnsureSuccess(Result<HttpResponseMessage>, Func<HttpStatusCode, Error>?)`. |

## Extension methods

### `HttpResponseExtensions`

```csharp
public static Result<HttpResponseMessage> HandleNotFound(this HttpResponseMessage response, NotFoundError notFoundError)
public static async Task<Result<HttpResponseMessage>> HandleNotFoundAsync(this Task<HttpResponseMessage> responseTask, NotFoundError notFoundError)
public static Result<HttpResponseMessage> HandleUnauthorized(this HttpResponseMessage response, UnauthorizedError unauthorizedError)
public static async Task<Result<HttpResponseMessage>> HandleUnauthorizedAsync(this Task<HttpResponseMessage> responseTask, UnauthorizedError unauthorizedError)
public static Result<HttpResponseMessage> HandleForbidden(this HttpResponseMessage response, ForbiddenError forbiddenError)
public static async Task<Result<HttpResponseMessage>> HandleForbiddenAsync(this Task<HttpResponseMessage> responseTask, ForbiddenError forbiddenError)
public static Result<HttpResponseMessage> HandleConflict(this HttpResponseMessage response, ConflictError conflictError)
public static async Task<Result<HttpResponseMessage>> HandleConflictAsync(this Task<HttpResponseMessage> responseTask, ConflictError conflictError)
public static Result<HttpResponseMessage> HandleClientError(this HttpResponseMessage response, Func<HttpStatusCode, Error> errorFactory)
public static async Task<Result<HttpResponseMessage>> HandleClientErrorAsync(this Task<HttpResponseMessage> responseTask, Func<HttpStatusCode, Error> errorFactory)
public static Result<HttpResponseMessage> HandleServerError(this HttpResponseMessage response, Func<HttpStatusCode, Error> errorFactory)
public static async Task<Result<HttpResponseMessage>> HandleServerErrorAsync(this Task<HttpResponseMessage> responseTask, Func<HttpStatusCode, Error> errorFactory)
public static Result<HttpResponseMessage> EnsureSuccess(this HttpResponseMessage response, Func<HttpStatusCode, Error>? errorFactory = null)
public static async Task<Result<HttpResponseMessage>> EnsureSuccessAsync(this Task<HttpResponseMessage> responseTask, Func<HttpStatusCode, Error>? errorFactory = null)
public static async Task<Result<HttpResponseMessage>> HandleFailureAsync<TContext>(this HttpResponseMessage response, Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callbackFailedStatusCode, TContext context, CancellationToken cancellationToken)
public static async Task<Result<HttpResponseMessage>> HandleFailureAsync<TContext>(this Task<HttpResponseMessage> responseTask, Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callbackFailedStatusCode, TContext context, CancellationToken cancellationToken)
public static async Task<Result<TValue>> ReadResultFromJsonAsync<TValue>(this HttpResponseMessage response, JsonTypeInfo<TValue> jsonTypeInfo, CancellationToken cancellationToken) where TValue : notnull
public static async Task<Result<TValue>> ReadResultFromJsonAsync<TValue>(this Task<HttpResponseMessage> responseTask, JsonTypeInfo<TValue> jsonTypeInfo, CancellationToken cancellationToken) where TValue : notnull
public static async Task<Result<TValue>> ReadResultFromJsonAsync<TValue>(this Result<HttpResponseMessage> response, JsonTypeInfo<TValue> jsonTypeInfo, CancellationToken cancellationToken) where TValue : notnull
public static async Task<Result<TValue>> ReadResultFromJsonAsync<TValue>(this Task<Result<HttpResponseMessage>> responseTask, JsonTypeInfo<TValue> jsonTypeInfo, CancellationToken cancellationToken) where TValue : notnull
public static async Task<Result<Maybe<TValue>>> ReadResultMaybeFromJsonAsync<TValue>(this HttpResponseMessage response, JsonTypeInfo<TValue> jsonTypeInfo, CancellationToken cancellationToken) where TValue : notnull
public static async Task<Result<Maybe<TValue>>> ReadResultMaybeFromJsonAsync<TValue>(this Task<HttpResponseMessage> responseTask, JsonTypeInfo<TValue> jsonTypeInfo, CancellationToken cancellationToken) where TValue : notnull
public static async Task<Result<Maybe<TValue>>> ReadResultMaybeFromJsonAsync<TValue>(this Result<HttpResponseMessage> response, JsonTypeInfo<TValue> jsonTypeInfo, CancellationToken cancellationToken) where TValue : notnull
public static async Task<Result<Maybe<TValue>>> ReadResultMaybeFromJsonAsync<TValue>(this Task<Result<HttpResponseMessage>> responseTask, JsonTypeInfo<TValue> jsonTypeInfo, CancellationToken cancellationToken) where TValue : notnull
public static Result<HttpResponseMessage> HandleNotFound(this Result<HttpResponseMessage> result, NotFoundError notFoundError)
public static async Task<Result<HttpResponseMessage>> HandleNotFoundAsync(this Task<Result<HttpResponseMessage>> resultTask, NotFoundError notFoundError)
public static Result<HttpResponseMessage> HandleUnauthorized(this Result<HttpResponseMessage> result, UnauthorizedError unauthorizedError)
public static async Task<Result<HttpResponseMessage>> HandleUnauthorizedAsync(this Task<Result<HttpResponseMessage>> resultTask, UnauthorizedError unauthorizedError)
public static Result<HttpResponseMessage> HandleForbidden(this Result<HttpResponseMessage> result, ForbiddenError forbiddenError)
public static async Task<Result<HttpResponseMessage>> HandleForbiddenAsync(this Task<Result<HttpResponseMessage>> resultTask, ForbiddenError forbiddenError)
public static Result<HttpResponseMessage> HandleConflict(this Result<HttpResponseMessage> result, ConflictError conflictError)
public static async Task<Result<HttpResponseMessage>> HandleConflictAsync(this Task<Result<HttpResponseMessage>> resultTask, ConflictError conflictError)
public static Result<HttpResponseMessage> HandleClientError(this Result<HttpResponseMessage> result, Func<HttpStatusCode, Error> errorFactory)
public static async Task<Result<HttpResponseMessage>> HandleClientErrorAsync(this Task<Result<HttpResponseMessage>> resultTask, Func<HttpStatusCode, Error> errorFactory)
public static Result<HttpResponseMessage> HandleServerError(this Result<HttpResponseMessage> result, Func<HttpStatusCode, Error> errorFactory)
public static async Task<Result<HttpResponseMessage>> HandleServerErrorAsync(this Task<Result<HttpResponseMessage>> resultTask, Func<HttpStatusCode, Error> errorFactory)
public static async Task<Result<HttpResponseMessage>> HandleFailureAsync<TContext>(this Result<HttpResponseMessage> result, Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callbackFailedStatusCode, TContext context, CancellationToken cancellationToken)
public static async Task<Result<HttpResponseMessage>> HandleFailureAsync<TContext>(this Task<Result<HttpResponseMessage>> resultTask, Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callbackFailedStatusCode, TContext context, CancellationToken cancellationToken)
public static Result<HttpResponseMessage> EnsureSuccess(this Result<HttpResponseMessage> result, Func<HttpStatusCode, Error>? errorFactory = null)
public static async Task<Result<HttpResponseMessage>> EnsureSuccessAsync(this Task<Result<HttpResponseMessage>> resultTask, Func<HttpStatusCode, Error>? errorFactory = null)
```

## Enums

This package exposes no public enums.

## Code examples

### Read a required JSON payload

```csharp
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Trellis;
using Trellis.Http;

public sealed record TodoDto(int Id, string Title);

[JsonSerializable(typeof(TodoDto))]
public partial class AppJsonContext : JsonSerializerContext
{
}

public static class TodoClient
{
    public static Task<Result<TodoDto>> GetTodoAsync(HttpClient httpClient, CancellationToken cancellationToken) =>
        httpClient.GetAsync("/todos/1", cancellationToken)
            .EnsureSuccessAsync()
            .ReadResultFromJsonAsync(AppJsonContext.Default.TodoDto, cancellationToken);
}
```

### Read an optional JSON payload

```csharp
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Trellis;
using Trellis.Http;

public sealed record ProfileDto(string DisplayName);

[JsonSerializable(typeof(ProfileDto))]
public partial class ProfileJsonContext : JsonSerializerContext
{
}

public static class ProfileClient
{
    public static Task<Result<Maybe<ProfileDto>>> GetProfileAsync(HttpClient httpClient, CancellationToken cancellationToken) =>
        httpClient.GetAsync("/profile", cancellationToken)
            .EnsureSuccessAsync()
            .ReadResultMaybeFromJsonAsync(ProfileJsonContext.Default.ProfileDto, cancellationToken);
}
```

## Cross-references

- [trellis-api-results.md](trellis-api-results.md)
- [trellis-api-asp.md](trellis-api-asp.md)
