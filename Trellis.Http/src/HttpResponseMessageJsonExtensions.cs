namespace Trellis.Http;

using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Trellis;

/// <summary>
/// Provides extension methods for handling HTTP response messages with Result and Maybe monads.
/// </summary>
/// <remarks>
/// <para>
/// These extension methods simplify common patterns when working with HTTP responses in a functional style:
/// <list type="bullet">
/// <item>Error handling for specific status codes (404 Not Found)</item>
/// <item>Custom error handling for failed responses</item>
/// <item>JSON deserialization with Result&lt;T&gt; and Maybe&lt;T&gt; support</item>
/// <item>Fluent composition with Railway Oriented Programming</item>
/// </list>
/// </para>
/// <para>
/// All methods are designed to integrate seamlessly with functional result types, enabling fluent error
/// handling and composition in asynchronous workflows. The caller is responsible for disposing of the
/// underlying HttpResponseMessage and handling cancellation via CancellationToken where applicable.
/// </para>
/// </remarks>
/// <example>
/// Typical usage with Railway Oriented Programming:
/// <code>
/// var result = await httpClient.GetAsync(url, ct)
///     .HandleNotFoundAsync(Error.NotFound("User", userId))
///     .ReadResultFromJsonAsync(UserJsonContext.Default.User, ct)
///     .TapAsync(user => _logger.LogInformation("Retrieved user: {UserId}", user.Id));
/// </code>
/// </example>
public static partial class HttpResponseExtensions
{
    /// <summary>
    /// Handles the case when the HTTP response has a status code of NotFound.
    /// </summary>
    /// <param name="response">The HTTP response message.</param>
    /// <param name="notFoundError">The error to return if the response has a status code of NotFound.</param>
    /// <returns>A <see cref="Result{TValue}"/> of <see cref="HttpResponseMessage"/>.</returns>
    public static Result<HttpResponseMessage> HandleNotFound(
        this HttpResponseMessage response,
        NotFoundError notFoundError)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
            return Result.Fail<HttpResponseMessage>(notFoundError);

        return Result.Ok(response);
    }

    /// <summary>
    /// Handles the case when the HTTP response has a status code of NotFound asynchronously.
    /// </summary>
    /// <param name="responseTask">The task representing the HTTP response message.</param>
    /// <param name="notFoundError">The error to return if the response has a status code of NotFound.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> HandleNotFoundAsync(
        this Task<HttpResponseMessage> responseTask,
        NotFoundError notFoundError)
    {
        var response = await responseTask.ConfigureAwait(false);
        return response.HandleNotFound(notFoundError);
    }

    /// <summary>
    /// Handles the case when the HTTP response has a status code of Unauthorized (401).
    /// </summary>
    /// <param name="response">The HTTP response message.</param>
    /// <param name="unauthorizedError">The error to return if the response has a status code of Unauthorized.</param>
    /// <returns>A <see cref="Result{TValue}"/> of <see cref="HttpResponseMessage"/>.</returns>
    public static Result<HttpResponseMessage> HandleUnauthorized(
        this HttpResponseMessage response,
        UnauthorizedError unauthorizedError)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return Result.Fail<HttpResponseMessage>(unauthorizedError);

        return Result.Ok(response);
    }

    /// <summary>
    /// Handles the case when the HTTP response has a status code of Unauthorized (401) asynchronously.
    /// </summary>
    /// <param name="responseTask">The task representing the HTTP response message.</param>
    /// <param name="unauthorizedError">The error to return if the response has a status code of Unauthorized.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> HandleUnauthorizedAsync(
        this Task<HttpResponseMessage> responseTask,
        UnauthorizedError unauthorizedError)
    {
        var response = await responseTask.ConfigureAwait(false);
        return response.HandleUnauthorized(unauthorizedError);
    }

    /// <summary>
    /// Handles the case when the HTTP response has a status code of Forbidden (403).
    /// </summary>
    /// <param name="response">The HTTP response message.</param>
    /// <param name="forbiddenError">The error to return if the response has a status code of Forbidden.</param>
    /// <returns>A <see cref="Result{TValue}"/> of <see cref="HttpResponseMessage"/>.</returns>
    public static Result<HttpResponseMessage> HandleForbidden(
        this HttpResponseMessage response,
        ForbiddenError forbiddenError)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden)
            return Result.Fail<HttpResponseMessage>(forbiddenError);

        return Result.Ok(response);
    }

    /// <summary>
    /// Handles the case when the HTTP response has a status code of Forbidden (403) asynchronously.
    /// </summary>
    /// <param name="responseTask">The task representing the HTTP response message.</param>
    /// <param name="forbiddenError">The error to return if the response has a status code of Forbidden.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> HandleForbiddenAsync(
        this Task<HttpResponseMessage> responseTask,
        ForbiddenError forbiddenError)
    {
        var response = await responseTask.ConfigureAwait(false);
        return response.HandleForbidden(forbiddenError);
    }

    /// <summary>
    /// Handles the case when the HTTP response has a status code of Conflict (409).
    /// </summary>
    /// <param name="response">The HTTP response message.</param>
    /// <param name="conflictError">The error to return if the response has a status code of Conflict.</param>
    /// <returns>A <see cref="Result{TValue}"/> of <see cref="HttpResponseMessage"/>.</returns>
    public static Result<HttpResponseMessage> HandleConflict(
        this HttpResponseMessage response,
        ConflictError conflictError)
    {
        if (response.StatusCode == HttpStatusCode.Conflict)
            return Result.Fail<HttpResponseMessage>(conflictError);

        return Result.Ok(response);
    }

    /// <summary>
    /// Handles the case when the HTTP response has a status code of Conflict (409) asynchronously.
    /// </summary>
    /// <param name="responseTask">The task representing the HTTP response message.</param>
    /// <param name="conflictError">The error to return if the response has a status code of Conflict.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> HandleConflictAsync(
        this Task<HttpResponseMessage> responseTask,
        ConflictError conflictError)
    {
        var response = await responseTask.ConfigureAwait(false);
        return response.HandleConflict(conflictError);
    }

    /// <summary>
    /// Handles any client error (4xx) status codes with a custom error factory.
    /// </summary>
    /// <param name="response">The HTTP response message.</param>
    /// <param name="errorFactory">A function that creates an error based on the status code.</param>
    /// <returns>A <see cref="Result{TValue}"/> of <see cref="HttpResponseMessage"/>.</returns>
    /// <remarks>
    /// This method intercepts HTTP responses with status codes in the 400-499 range.
    /// Use this when you want to handle all client errors uniformly, or when you need
    /// to create context-specific errors based on the actual status code received.
    /// </remarks>
    /// <example>
    /// <code>
    /// var result = await httpClient.GetAsync(url, ct)
    ///     .HandleClientErrorAsync(code => Error.BadRequest($"Client error: {code}"))
    ///     .ReadResultFromJsonAsync(jsonContext, ct);
    /// </code>
    /// </example>
    public static Result<HttpResponseMessage> HandleClientError(
        this HttpResponseMessage response,
        Func<HttpStatusCode, Error> errorFactory)
    {
        var statusCode = (int)response.StatusCode;
        if (statusCode is >= 400 and < 500)
            return Result.Fail<HttpResponseMessage>(errorFactory(response.StatusCode));

        return Result.Ok(response);
    }

    /// <summary>
    /// Handles any client error (4xx) status codes with a custom error factory asynchronously.
    /// </summary>
    /// <param name="responseTask">The task representing the HTTP response message.</param>
    /// <param name="errorFactory">A function that creates an error based on the status code.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> HandleClientErrorAsync(
        this Task<HttpResponseMessage> responseTask,
        Func<HttpStatusCode, Error> errorFactory)
    {
        var response = await responseTask.ConfigureAwait(false);
        return response.HandleClientError(errorFactory);
    }

    /// <summary>
    /// Handles any server error (5xx) status codes with a custom error factory.
    /// </summary>
    /// <param name="response">The HTTP response message.</param>
    /// <param name="errorFactory">A function that creates an error based on the status code.</param>
    /// <returns>A <see cref="Result{TValue}"/> of <see cref="HttpResponseMessage"/>.</returns>
    /// <remarks>
    /// This method intercepts HTTP responses with status codes in the 500-599 range.
    /// Use this when you want to handle all server errors uniformly, such as for retry
    /// logic or fallback mechanisms.
    /// </remarks>
    /// <example>
    /// <code>
    /// var result = await httpClient.GetAsync(url, ct)
    ///     .HandleServerErrorAsync(code => Error.ServiceUnavailable($"Server error: {code}"))
    ///     .ReadResultFromJsonAsync(jsonContext, ct);
    /// </code>
    /// </example>
    public static Result<HttpResponseMessage> HandleServerError(
        this HttpResponseMessage response,
        Func<HttpStatusCode, Error> errorFactory)
    {
        var statusCode = (int)response.StatusCode;
        if (statusCode is >= 500 and < 600)
            return Result.Fail<HttpResponseMessage>(errorFactory(response.StatusCode));

        return Result.Ok(response);
    }

    /// <summary>
    /// Handles any server error (5xx) status codes with a custom error factory asynchronously.
    /// </summary>
    /// <param name="responseTask">The task representing the HTTP response message.</param>
    /// <param name="errorFactory">A function that creates an error based on the status code.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> HandleServerErrorAsync(
        this Task<HttpResponseMessage> responseTask,
        Func<HttpStatusCode, Error> errorFactory)
    {
        var response = await responseTask.ConfigureAwait(false);
        return response.HandleServerError(errorFactory);
    }

    /// <summary>
    /// Ensures the HTTP response has a success status code, otherwise returns an error.
    /// </summary>
    /// <param name="response">The HTTP response message.</param>
    /// <param name="errorFactory">Optional function to create a custom error based on the status code.
    /// If not provided, a default unexpected error will be created.</param>
    /// <returns>A <see cref="Result{TValue}"/> of <see cref="HttpResponseMessage"/>.</returns>
    /// <remarks>
    /// This is a functional alternative to HttpResponseMessage.EnsureSuccessStatusCode(),
    /// which throws an exception. This method returns a Result instead, making it compatible
    /// with Railway Oriented Programming patterns.
    /// </remarks>
    /// <example>
    /// <code>
    /// var result = await httpClient.GetAsync(url, ct)
    ///     .EnsureSuccessAsync()
    ///     .ReadResultFromJsonAsync(jsonContext, ct);
    /// </code>
    /// </example>
    public static Result<HttpResponseMessage> EnsureSuccess(
        this HttpResponseMessage response,
        Func<HttpStatusCode, Error>? errorFactory = null)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = errorFactory?.Invoke(response.StatusCode)
                ?? Error.Unexpected($"HTTP request failed with status code {response.StatusCode}.");
            return Result.Fail<HttpResponseMessage>(error);
        }

        return Result.Ok(response);
    }

    /// <summary>
    /// Ensures the HTTP response has a success status code, otherwise returns an error asynchronously.
    /// </summary>
    /// <param name="responseTask">The task representing the HTTP response message.</param>
    /// <param name="errorFactory">Optional function to create a custom error based on the status code.
    /// If not provided, a default unexpected error will be created.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> EnsureSuccessAsync(
        this Task<HttpResponseMessage> responseTask,
        Func<HttpStatusCode, Error>? errorFactory = null)
    {
        var response = await responseTask.ConfigureAwait(false);
        return response.EnsureSuccess(errorFactory);
    }

    /// <summary>
    /// Handles the case when the HTTP response is not successful.
    /// </summary>
    /// <typeparam name="TContext">The type of the context object.</typeparam>
    /// <param name="response">The HTTP response message.</param>
    /// <param name="callbackFailedStatusCode">The callback function to handle the failed status code.</param>
    /// <param name="context">The context object.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> HandleFailureAsync<TContext>(
        this HttpResponseMessage response,
        Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callbackFailedStatusCode,
        TContext context,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await callbackFailedStatusCode(response, context, cancellationToken).ConfigureAwait(false);
            return Result.Fail<HttpResponseMessage>(error);
        }

        return Result.Ok(response);
    }

    /// <summary>
    /// Handles the case when the HTTP response is not successful asynchronously.
    /// </summary>
    /// <typeparam name="TContext">The type of the context object.</typeparam>
    /// <param name="responseTask">The task representing the HTTP response message.</param>
    /// <param name="callbackFailedStatusCode">The callback function to handle the failed status code.</param>
    /// <param name="context">The context object.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> HandleFailureAsync<TContext>(
        this Task<HttpResponseMessage> responseTask,
        Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callbackFailedStatusCode,
        TContext context,
        CancellationToken cancellationToken)
    {
        var response = await responseTask.ConfigureAwait(false);
        return await response.HandleFailureAsync(callbackFailedStatusCode, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the HTTP response content as JSON and deserializes it to the specified type.
    /// </summary>
    /// <typeparam name="TValue">The type to deserialize the JSON content to.</typeparam>
    /// <param name="response">The HTTP response message.</param>
    /// <param name="jsonTypeInfo">The JSON type information for deserialization.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the deserialized value.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task<Result<TValue>> ReadResultFromJsonAsync<TValue>(
        this HttpResponseMessage response,
        JsonTypeInfo<TValue> jsonTypeInfo,
        CancellationToken cancellationToken)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        if (response.IsSuccessStatusCode == false)
            return Result.Fail<TValue>(Error.Unexpected($"HTTP response is in a failed state for value {typeof(TValue).Name}. Status code: {response.StatusCode}."));

        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.ResetContent)
            return Result.Fail<TValue>(Error.Unexpected($"HTTP response was null for value {typeof(TValue).Name}."));

        TValue? value;
        try
        {
            value = await response.Content.ReadFromJsonAsync(jsonTypeInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return Result.Fail<TValue>(Error.Unexpected($"Failed to deserialize HTTP response to {typeof(TValue).Name}: {ex.Message}"));
        }

        return value is null
            ? Result.Fail<TValue>(Error.Unexpected($"HTTP response was null for value {typeof(TValue).Name}."))
            : Result.Ok(value);
    }

    /// <summary>
    /// Reads the HTTP response content as JSON and deserializes it to the specified type asynchronously.
    /// </summary>
    /// <typeparam name="TValue">The type to deserialize the JSON content to.</typeparam>
    /// <param name="responseTask">The task representing the HTTP response message.</param>
    /// <param name="jsonTypeInfo">The JSON type information for deserialization.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the deserialized value.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task<Result<TValue>> ReadResultFromJsonAsync<TValue>(
        this Task<HttpResponseMessage> responseTask,
        JsonTypeInfo<TValue> jsonTypeInfo,
        CancellationToken cancellationToken)
        where TValue : notnull
    {
        var response = await responseTask.ConfigureAwait(false);
        return await response.ReadResultFromJsonAsync(jsonTypeInfo, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the HTTP response content as JSON and deserializes it to the specified type using the Result monad.
    /// </summary>
    /// <typeparam name="TValue">The type to deserialize the JSON content to.</typeparam>
    /// <param name="response">The Result object containing the HTTP response message.</param>
    /// <param name="jsonTypeInfo">The JSON type information for deserialization.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the deserialized value.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task<Result<TValue>> ReadResultFromJsonAsync<TValue>(
        this Result<HttpResponseMessage> response,
        JsonTypeInfo<TValue> jsonTypeInfo,
        CancellationToken cancellationToken)
        where TValue : notnull
        => await response.BindAsync(r => r.ReadResultFromJsonAsync(jsonTypeInfo, cancellationToken)).ConfigureAwait(false);

    /// <summary>
    /// Reads the HTTP response content as JSON and deserializes it to the specified type using the Result monad asynchronously.
    /// </summary>
    /// <typeparam name="TValue">The type to deserialize the JSON content to.</typeparam>
    /// <param name="responseTask">The task representing the Result object containing the HTTP response message.</param>
    /// <param name="jsonTypeInfo">The JSON type information for deserialization.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the deserialized value.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task<Result<TValue>> ReadResultFromJsonAsync<TValue>(
        this Task<Result<HttpResponseMessage>> responseTask,
        JsonTypeInfo<TValue> jsonTypeInfo,
        CancellationToken cancellationToken)
        where TValue : notnull
    {
        var response = await responseTask.ConfigureAwait(false);
        return await response.ReadResultFromJsonAsync(jsonTypeInfo, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the HTTP response content as JSON and deserializes it to the specified type using the Maybe monad.
    /// </summary>
    /// <typeparam name="TValue">The type to deserialize the JSON content to.</typeparam>
    /// <param name="response">The HTTP response message.</param>
    /// <param name="jsonTypeInfo">The JSON type information for deserialization.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing <see cref="Maybe{TValue}"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">
    /// Thrown when the response has a successful status code but the content is not valid JSON for <typeparamref name="TValue"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task<Result<Maybe<TValue>>> ReadResultMaybeFromJsonAsync<TValue>(
        this HttpResponseMessage response,
        JsonTypeInfo<TValue> jsonTypeInfo,
        CancellationToken cancellationToken)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        if (response.IsSuccessStatusCode == false)
            return Result.Fail<Maybe<TValue>>(Error.Unexpected($"HTTP response is in a failed state for value {typeof(TValue).Name}. Status code: {response.StatusCode}."));

        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.ResetContent)
            return Result.Ok(Maybe<TValue>.None);

        if (response.Content is null)
            return Result.Ok(Maybe<TValue>.None);

        var contentBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (contentBytes.Length == 0)
            return Result.Ok(Maybe<TValue>.None);

        var value = JsonSerializer.Deserialize(contentBytes, jsonTypeInfo);

        return Result.Ok(value is null ? Maybe<TValue>.None : Maybe.From(value));
    }

    /// <summary>
    /// Reads the HTTP response content as JSON and deserializes it to the specified type using the Maybe monad asynchronously.
    /// </summary>
    /// <typeparam name="TValue">The type to deserialize the JSON content to.</typeparam>
    /// <param name="responseTask">The task representing the HTTP response message.</param>
    /// <param name="jsonTypeInfo">The JSON type information for deserialization.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing <see cref="Maybe{TValue}"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">
    /// Thrown when the response has a successful status code but the content is not valid JSON for <typeparamref name="TValue"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task<Result<Maybe<TValue>>> ReadResultMaybeFromJsonAsync<TValue>(
        this Task<HttpResponseMessage> responseTask,
        JsonTypeInfo<TValue> jsonTypeInfo,
        CancellationToken cancellationToken)
        where TValue : notnull
    {
        var response = await responseTask.ConfigureAwait(false);
        return await response.ReadResultMaybeFromJsonAsync(jsonTypeInfo, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the HTTP response content as JSON and deserializes it to the specified type using the Maybe monad and the Result monad.
    /// </summary>
    /// <typeparam name="TValue">The type to deserialize the JSON content to.</typeparam>
    /// <param name="response">The Result object containing the HTTP response message.</param>
    /// <param name="jsonTypeInfo">The JSON type information for deserialization.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing <see cref="Maybe{TValue}"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">
    /// Thrown when the wrapped response has a successful status code but the content is not valid JSON for <typeparamref name="TValue"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task<Result<Maybe<TValue>>> ReadResultMaybeFromJsonAsync<TValue>(
        this Result<HttpResponseMessage> response,
        JsonTypeInfo<TValue> jsonTypeInfo,
        CancellationToken cancellationToken)
        where TValue : notnull
        => await response.BindAsync(r => r.ReadResultMaybeFromJsonAsync(jsonTypeInfo, cancellationToken)).ConfigureAwait(false);

    /// <summary>
    /// Reads the HTTP response content as JSON and deserializes it to the specified type using the Maybe monad and the Result monad asynchronously.
    /// </summary>
    /// <typeparam name="TValue">The type to deserialize the JSON content to.</typeparam>
    /// <param name="responseTask">The task representing the Result object containing the HTTP response message.</param>
    /// <param name="jsonTypeInfo">The JSON type information for deserialization.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing <see cref="Maybe{TValue}"/>.</returns>
    /// <exception cref="System.Text.Json.JsonException">
    /// Thrown when the wrapped response has a successful status code but the content is not valid JSON for <typeparamref name="TValue"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is canceled.</exception>
    public static async Task<Result<Maybe<TValue>>> ReadResultMaybeFromJsonAsync<TValue>(
        this Task<Result<HttpResponseMessage>> responseTask,
        JsonTypeInfo<TValue> jsonTypeInfo,
        CancellationToken cancellationToken)
        where TValue : notnull
    {
        var response = await responseTask.ConfigureAwait(false);
        return await response.ReadResultMaybeFromJsonAsync(jsonTypeInfo, cancellationToken).ConfigureAwait(false);
    }
}