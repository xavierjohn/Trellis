namespace Trellis.Http;

using System.Net;
using Trellis;

/// <summary>
/// Provides <see cref="Result{TValue}"/> and <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/>
/// overloads for HTTP status code handlers, enabling fluent chaining without explicit <c>Bind</c> calls.
/// </summary>
public static partial class HttpResponseExtensions
{
    #region HandleNotFound — Result overloads

    /// <summary>
    /// Handles the case when the HTTP response has a status code of NotFound.
    /// </summary>
    /// <param name="result">The result containing the HTTP response message.</param>
    /// <param name="error">The error to return if the response has a status code of NotFound.</param>
    /// <returns>A <see cref="Result{TValue}"/> of <see cref="HttpResponseMessage"/>.</returns>
    public static Result<HttpResponseMessage> HandleNotFound(
        this Result<HttpResponseMessage> result,
        Error.NotFound error)
        => result.Bind(r => r.HandleNotFound(error));

    /// <summary>
    /// Handles the case when the HTTP response has a status code of NotFound asynchronously.
    /// </summary>
    /// <param name="resultTask">The task representing the result containing the HTTP response message.</param>
    /// <param name="error">The error to return if the response has a status code of NotFound.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> HandleNotFoundAsync(
        this Task<Result<HttpResponseMessage>> resultTask,
        Error.NotFound error)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.HandleNotFound(error);
    }

    #endregion

    #region HandleUnauthorized — Result overloads

    /// <summary>
    /// Handles the case when the HTTP response has a status code of Unauthorized (401).
    /// </summary>
    /// <param name="result">The result containing the HTTP response message.</param>
    /// <param name="error">The error to return if the response has a status code of Unauthorized.</param>
    /// <returns>A <see cref="Result{TValue}"/> of <see cref="HttpResponseMessage"/>.</returns>
    public static Result<HttpResponseMessage> HandleUnauthorized(
        this Result<HttpResponseMessage> result,
        Error.Unauthorized error)
        => result.Bind(r => r.HandleUnauthorized(error));

    /// <summary>
    /// Handles the case when the HTTP response has a status code of Unauthorized (401) asynchronously.
    /// </summary>
    /// <param name="resultTask">The task representing the result containing the HTTP response message.</param>
    /// <param name="error">The error to return if the response has a status code of Unauthorized.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> HandleUnauthorizedAsync(
        this Task<Result<HttpResponseMessage>> resultTask,
        Error.Unauthorized error)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.HandleUnauthorized(error);
    }

    #endregion

    #region HandleForbidden — Result overloads

    /// <summary>
    /// Handles the case when the HTTP response has a status code of Forbidden (403).
    /// </summary>
    /// <param name="result">The result containing the HTTP response message.</param>
    /// <param name="error">The error to return if the response has a status code of Forbidden.</param>
    /// <returns>A <see cref="Result{TValue}"/> of <see cref="HttpResponseMessage"/>.</returns>
    public static Result<HttpResponseMessage> HandleForbidden(
        this Result<HttpResponseMessage> result,
        Error.Forbidden error)
        => result.Bind(r => r.HandleForbidden(error));

    /// <summary>
    /// Handles the case when the HTTP response has a status code of Forbidden (403) asynchronously.
    /// </summary>
    /// <param name="resultTask">The task representing the result containing the HTTP response message.</param>
    /// <param name="error">The error to return if the response has a status code of Forbidden.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> HandleForbiddenAsync(
        this Task<Result<HttpResponseMessage>> resultTask,
        Error.Forbidden error)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.HandleForbidden(error);
    }

    #endregion

    #region HandleConflict — Result overloads

    /// <summary>
    /// Handles the case when the HTTP response has a status code of Conflict (409).
    /// </summary>
    /// <param name="result">The result containing the HTTP response message.</param>
    /// <param name="error">The error to return if the response has a status code of Conflict.</param>
    /// <returns>A <see cref="Result{TValue}"/> of <see cref="HttpResponseMessage"/>.</returns>
    public static Result<HttpResponseMessage> HandleConflict(
        this Result<HttpResponseMessage> result,
        Error.Conflict error)
        => result.Bind(r => r.HandleConflict(error));

    /// <summary>
    /// Handles the case when the HTTP response has a status code of Conflict (409) asynchronously.
    /// </summary>
    /// <param name="resultTask">The task representing the result containing the HTTP response message.</param>
    /// <param name="error">The error to return if the response has a status code of Conflict.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> HandleConflictAsync(
        this Task<Result<HttpResponseMessage>> resultTask,
        Error.Conflict error)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.HandleConflict(error);
    }

    #endregion

    #region HandleClientError — Result overloads

    /// <summary>
    /// Handles any client error (4xx) status codes with a custom error factory.
    /// </summary>
    /// <param name="result">The result containing the HTTP response message.</param>
    /// <param name="errorFactory">A function that creates an error based on the status code.</param>
    /// <returns>A <see cref="Result{TValue}"/> of <see cref="HttpResponseMessage"/>.</returns>
    public static Result<HttpResponseMessage> HandleClientError(
        this Result<HttpResponseMessage> result,
        Func<HttpStatusCode, Error> errorFactory)
        => result.Bind(r => r.HandleClientError(errorFactory));

    /// <summary>
    /// Handles any client error (4xx) status codes with a custom error factory asynchronously.
    /// </summary>
    /// <param name="resultTask">The task representing the result containing the HTTP response message.</param>
    /// <param name="errorFactory">A function that creates an error based on the status code.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> HandleClientErrorAsync(
        this Task<Result<HttpResponseMessage>> resultTask,
        Func<HttpStatusCode, Error> errorFactory)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.HandleClientError(errorFactory);
    }

    #endregion

    #region HandleServerError — Result overloads

    /// <summary>
    /// Handles any server error (5xx) status codes with a custom error factory.
    /// </summary>
    /// <param name="result">The result containing the HTTP response message.</param>
    /// <param name="errorFactory">A function that creates an error based on the status code.</param>
    /// <returns>A <see cref="Result{TValue}"/> of <see cref="HttpResponseMessage"/>.</returns>
    public static Result<HttpResponseMessage> HandleServerError(
        this Result<HttpResponseMessage> result,
        Func<HttpStatusCode, Error> errorFactory)
        => result.Bind(r => r.HandleServerError(errorFactory));

    /// <summary>
    /// Handles any server error (5xx) status codes with a custom error factory asynchronously.
    /// </summary>
    /// <param name="resultTask">The task representing the result containing the HTTP response message.</param>
    /// <param name="errorFactory">A function that creates an error based on the status code.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> HandleServerErrorAsync(
        this Task<Result<HttpResponseMessage>> resultTask,
        Func<HttpStatusCode, Error> errorFactory)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.HandleServerError(errorFactory);
    }

    #endregion

    #region HandleFailure — Result overloads

    /// <summary>
    /// Handles the case when the HTTP response is not successful.
    /// </summary>
    /// <typeparam name="TContext">The type of the context object.</typeparam>
    /// <param name="result">The result containing the HTTP response message.</param>
    /// <param name="callbackFailedStatusCode">The callback function to handle the failed status code.</param>
    /// <param name="context">The context object.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> HandleFailureAsync<TContext>(
        this Result<HttpResponseMessage> result,
        Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callbackFailedStatusCode,
        TContext context,
        CancellationToken cancellationToken)
        => await result.BindAsync(r => r.HandleFailureAsync(callbackFailedStatusCode, context, cancellationToken)).ConfigureAwait(false);

    /// <summary>
    /// Handles the case when the HTTP response is not successful asynchronously.
    /// </summary>
    /// <typeparam name="TContext">The type of the context object.</typeparam>
    /// <param name="resultTask">The task representing the result containing the HTTP response message.</param>
    /// <param name="callbackFailedStatusCode">The callback function to handle the failed status code.</param>
    /// <param name="context">The context object.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> HandleFailureAsync<TContext>(
        this Task<Result<HttpResponseMessage>> resultTask,
        Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callbackFailedStatusCode,
        TContext context,
        CancellationToken cancellationToken)
    {
        var result = await resultTask.ConfigureAwait(false);
        return await result.HandleFailureAsync(callbackFailedStatusCode, context, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region EnsureSuccess — Result overloads

    /// <summary>
    /// Ensures the HTTP response has a success status code, otherwise returns an error.
    /// </summary>
    /// <param name="result">The result containing the HTTP response message.</param>
    /// <param name="errorFactory">Optional function to create a custom error based on the status code.
    /// If not provided, a default unexpected error will be created.</param>
    /// <returns>A <see cref="Result{TValue}"/> of <see cref="HttpResponseMessage"/>.</returns>
    public static Result<HttpResponseMessage> EnsureSuccess(
        this Result<HttpResponseMessage> result,
        Func<HttpStatusCode, Error>? errorFactory = null)
        => result.Bind(r => r.EnsureSuccess(errorFactory));

    /// <summary>
    /// Ensures the HTTP response has a success status code, otherwise returns an error asynchronously.
    /// </summary>
    /// <param name="resultTask">The task representing the result containing the HTTP response message.</param>
    /// <param name="errorFactory">Optional function to create a custom error based on the status code.
    /// If not provided, a default unexpected error will be created.</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="Result{TValue}"/> containing the <see cref="HttpResponseMessage"/>.</returns>
    public static async Task<Result<HttpResponseMessage>> EnsureSuccessAsync(
        this Task<Result<HttpResponseMessage>> resultTask,
        Func<HttpStatusCode, Error>? errorFactory = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.EnsureSuccess(errorFactory);
    }

    #endregion
}