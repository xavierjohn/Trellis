namespace Trellis;

using System.Diagnostics;
using System.Threading.Tasks;

/// <summary>
/// Pattern matching helpers for discriminating specific error types in Result.
/// Allows matching on specific error types (ValidationError, NotFoundError, etc.) 
/// rather than treating all errors the same.
/// </summary>
[DebuggerStepThrough]
public static class MatchErrorExtensions
{
    /// <summary>
    /// Pattern matches on the result, with specific handlers for different error types.
    /// </summary>
    /// <typeparam name="TIn">Type of the result value.</typeparam>
    /// <typeparam name="TOut">Type of the output.</typeparam>
    /// <param name="result">The result to match on.</param>
    /// <param name="onSuccess">Function to execute on success.</param>
    /// <param name="onValidation">Function to execute when the error is a ValidationError.</param>
    /// <param name="onNotFound">Function to execute when the error is a NotFoundError.</param>
    /// <param name="onConflict">Function to execute when the error is a ConflictError.</param>
    /// <param name="onBadRequest">Function to execute when the error is a BadRequestError.</param>
    /// <param name="onUnauthorized">Function to execute when the error is an UnauthorizedError.</param>
    /// <param name="onForbidden">Function to execute when the error is a ForbiddenError.</param>
    /// <param name="onDomain">Function to execute when the error is a DomainError.</param>
    /// <param name="onRateLimit">Function to execute when the error is a RateLimitError.</param>
    /// <param name="onServiceUnavailable">Function to execute when the error is a ServiceUnavailableError.</param>
    /// <param name="onUnexpected">Function to execute when the error is an UnexpectedError.</param>
    /// <param name="onError">Default function to execute for any other error type.</param>
    /// <returns>The output from the appropriate handler function.</returns>
    /// <remarks>
    /// Error handlers are evaluated in order. The first matching error type handler is executed.
    /// If no specific error type matches and onError is not provided, an InvalidOperationException is thrown.
    /// </remarks>
    /// <example>
    /// <code>
    /// var message = GetUser(userId).MatchError(
    ///     onSuccess: user => $"Found: {user.Name}",
    ///     onNotFound: err => "User not found",
    ///     onValidation: err => $"Invalid: {err.Detail}",
    ///     onError: err => "An error occurred"
    /// );
    /// </code>
    /// </example>
    public static TOut MatchError<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, TOut> onSuccess,
        Func<ValidationError, TOut>? onValidation = null,
        Func<NotFoundError, TOut>? onNotFound = null,
        Func<ConflictError, TOut>? onConflict = null,
        Func<BadRequestError, TOut>? onBadRequest = null,
        Func<UnauthorizedError, TOut>? onUnauthorized = null,
        Func<ForbiddenError, TOut>? onForbidden = null,
        Func<DomainError, TOut>? onDomain = null,
        Func<RateLimitError, TOut>? onRateLimit = null,
        Func<ServiceUnavailableError, TOut>? onServiceUnavailable = null,
        Func<UnexpectedError, TOut>? onUnexpected = null,
        Func<Error, TOut>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);

        using var activity = RopTrace.ActivitySource.StartActivity();

        if (result.IsSuccess)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            return onSuccess(result.Value);
        }

        activity?.SetStatus(ActivityStatusCode.Error);

        var error = result.Error;

        return error switch
        {
            ValidationError ve when onValidation != null => onValidation(ve),
            NotFoundError nf when onNotFound != null => onNotFound(nf),
            ConflictError ce when onConflict != null => onConflict(ce),
            BadRequestError br when onBadRequest != null => onBadRequest(br),
            UnauthorizedError ua when onUnauthorized != null => onUnauthorized(ua),
            ForbiddenError fe when onForbidden != null => onForbidden(fe),
            DomainError de when onDomain != null => onDomain(de),
            RateLimitError rl when onRateLimit != null => onRateLimit(rl),
            ServiceUnavailableError su when onServiceUnavailable != null => onServiceUnavailable(su),
            UnexpectedError ue when onUnexpected != null => onUnexpected(ue),
            _ when onError != null => onError(error),
            _ => throw new InvalidOperationException(
                $"No handler provided for error type {error.GetType().Name}. " +
                "Either provide a specific handler or use onError as a catch-all.")
        };
    }

    /// <summary>
    /// Executes different actions based on the result state and error type.
    /// </summary>
    /// <typeparam name="TIn">Type of the result value.</typeparam>
    /// <param name="result">The result to switch on.</param>
    /// <param name="onSuccess">Action to execute on success.</param>
    /// <param name="onValidation">Action to execute when the error is a ValidationError.</param>
    /// <param name="onNotFound">Action to execute when the error is a NotFoundError.</param>
    /// <param name="onConflict">Action to execute when the error is a ConflictError.</param>
    /// <param name="onBadRequest">Action to execute when the error is a BadRequestError.</param>
    /// <param name="onUnauthorized">Action to execute when the error is an UnauthorizedError.</param>
    /// <param name="onForbidden">Action to execute when the error is a ForbiddenError.</param>
    /// <param name="onDomain">Action to execute when the error is a DomainError.</param>
    /// <param name="onRateLimit">Action to execute when the error is a RateLimitError.</param>
    /// <param name="onServiceUnavailable">Action to execute when the error is a ServiceUnavailableError.</param>
    /// <param name="onUnexpected">Action to execute when the error is an UnexpectedError.</param>
    /// <param name="onError">Default action to execute for any other error type.</param>
    /// <remarks>
    /// Error handlers are evaluated in order. The first matching error type handler is executed.
    /// If no specific error type matches and onError is not provided, an InvalidOperationException is thrown.
    /// </remarks>
    public static void SwitchError<TIn>(
        this Result<TIn> result,
        Action<TIn> onSuccess,
        Action<ValidationError>? onValidation = null,
        Action<NotFoundError>? onNotFound = null,
        Action<ConflictError>? onConflict = null,
        Action<BadRequestError>? onBadRequest = null,
        Action<UnauthorizedError>? onUnauthorized = null,
        Action<ForbiddenError>? onForbidden = null,
        Action<DomainError>? onDomain = null,
        Action<RateLimitError>? onRateLimit = null,
        Action<ServiceUnavailableError>? onServiceUnavailable = null,
        Action<UnexpectedError>? onUnexpected = null,
        Action<Error>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);

        using var activity = RopTrace.ActivitySource.StartActivity();

        if (result.IsSuccess)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            onSuccess(result.Value);
            return;
        }

        activity?.SetStatus(ActivityStatusCode.Error);

        var error = result.Error;

        switch (error)
        {
            case ValidationError ve when onValidation != null:
                onValidation(ve);
                break;
            case NotFoundError nf when onNotFound != null:
                onNotFound(nf);
                break;
            case ConflictError ce when onConflict != null:
                onConflict(ce);
                break;
            case BadRequestError br when onBadRequest != null:
                onBadRequest(br);
                break;
            case UnauthorizedError ua when onUnauthorized != null:
                onUnauthorized(ua);
                break;
            case ForbiddenError fe when onForbidden != null:
                onForbidden(fe);
                break;
            case DomainError de when onDomain != null:
                onDomain(de);
                break;
            case RateLimitError rl when onRateLimit != null:
                onRateLimit(rl);
                break;
            case ServiceUnavailableError su when onServiceUnavailable != null:
                onServiceUnavailable(su);
                break;
            case UnexpectedError ue when onUnexpected != null:
                onUnexpected(ue);
                break;
            default:
                if (onError != null)
                    onError(error);
                else
                    throw new InvalidOperationException(
                        $"No handler provided for error type {error.GetType().Name}. " +
                        "Either provide a specific handler or use onError as a catch-all.");
                break;
        }
    }
}

/// <summary>
/// Asynchronous pattern matching helpers for discriminating specific error types in Result.
/// </summary>
[DebuggerStepThrough]
public static class MatchErrorExtensionsAsync
{
    /// <summary>
    /// Asynchronously pattern matches on the result, with specific handlers for different error types.
    /// </summary>
    /// <typeparam name="TIn">Type of the result value.</typeparam>
    /// <typeparam name="TOut">Type of the output.</typeparam>
    /// <param name="resultTask">The task representing the result to match on.</param>
    /// <param name="onSuccess">Function to execute on success.</param>
    /// <param name="onValidation">Function to execute when the error is a ValidationError.</param>
    /// <param name="onNotFound">Function to execute when the error is a NotFoundError.</param>
    /// <param name="onConflict">Function to execute when the error is a ConflictError.</param>
    /// <param name="onBadRequest">Function to execute when the error is a BadRequestError.</param>
    /// <param name="onUnauthorized">Function to execute when the error is an UnauthorizedError.</param>
    /// <param name="onForbidden">Function to execute when the error is a ForbiddenError.</param>
    /// <param name="onDomain">Function to execute when the error is a DomainError.</param>
    /// <param name="onRateLimit">Function to execute when the error is a RateLimitError.</param>
    /// <param name="onServiceUnavailable">Function to execute when the error is a ServiceUnavailableError.</param>
    /// <param name="onUnexpected">Function to execute when the error is an UnexpectedError.</param>
    /// <param name="onError">Default function to execute for any other error type.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the output from the appropriate handler function.</returns>
    public static async Task<TOut> MatchErrorAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, TOut> onSuccess,
        Func<ValidationError, TOut>? onValidation = null,
        Func<NotFoundError, TOut>? onNotFound = null,
        Func<ConflictError, TOut>? onConflict = null,
        Func<BadRequestError, TOut>? onBadRequest = null,
        Func<UnauthorizedError, TOut>? onUnauthorized = null,
        Func<ForbiddenError, TOut>? onForbidden = null,
        Func<DomainError, TOut>? onDomain = null,
        Func<RateLimitError, TOut>? onRateLimit = null,
        Func<ServiceUnavailableError, TOut>? onServiceUnavailable = null,
        Func<UnexpectedError, TOut>? onUnexpected = null,
        Func<Error, TOut>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(onSuccess);

        var result = await resultTask.ConfigureAwait(false);
        return result.MatchError(
            onSuccess,
            onValidation,
            onNotFound,
            onConflict,
            onBadRequest,
            onUnauthorized,
            onForbidden,
            onDomain,
            onRateLimit,
            onServiceUnavailable,
            onUnexpected,
            onError);
    }

    /// <summary>
    /// Asynchronously pattern matches on the result with async handlers for different error types.
    /// </summary>
    /// <typeparam name="TIn">Type of the result value.</typeparam>
    /// <typeparam name="TOut">Type of the output.</typeparam>
    /// <param name="result">The result to match on.</param>
    /// <param name="onSuccess">Async function to execute on success.</param>
    /// <param name="onValidation">Async function to execute when the error is a ValidationError.</param>
    /// <param name="onNotFound">Async function to execute when the error is a NotFoundError.</param>
    /// <param name="onConflict">Async function to execute when the error is a ConflictError.</param>
    /// <param name="onBadRequest">Async function to execute when the error is a BadRequestError.</param>
    /// <param name="onUnauthorized">Async function to execute when the error is an UnauthorizedError.</param>
    /// <param name="onForbidden">Async function to execute when the error is a ForbiddenError.</param>
    /// <param name="onDomain">Async function to execute when the error is a DomainError.</param>
    /// <param name="onRateLimit">Async function to execute when the error is a RateLimitError.</param>
    /// <param name="onServiceUnavailable">Async function to execute when the error is a ServiceUnavailableError.</param>
    /// <param name="onUnexpected">Async function to execute when the error is an UnexpectedError.</param>
    /// <param name="onError">Default async function to execute for any other error type.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the output from the appropriate handler function.</returns>
    public static async Task<TOut> MatchErrorAsync<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, CancellationToken, Task<TOut>> onSuccess,
        Func<ValidationError, CancellationToken, Task<TOut>>? onValidation = null,
        Func<NotFoundError, CancellationToken, Task<TOut>>? onNotFound = null,
        Func<ConflictError, CancellationToken, Task<TOut>>? onConflict = null,
        Func<BadRequestError, CancellationToken, Task<TOut>>? onBadRequest = null,
        Func<UnauthorizedError, CancellationToken, Task<TOut>>? onUnauthorized = null,
        Func<ForbiddenError, CancellationToken, Task<TOut>>? onForbidden = null,
        Func<DomainError, CancellationToken, Task<TOut>>? onDomain = null,
        Func<RateLimitError, CancellationToken, Task<TOut>>? onRateLimit = null,
        Func<ServiceUnavailableError, CancellationToken, Task<TOut>>? onServiceUnavailable = null,
        Func<UnexpectedError, CancellationToken, Task<TOut>>? onUnexpected = null,
        Func<Error, CancellationToken, Task<TOut>>? onError = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);

        using var activity = RopTrace.ActivitySource.StartActivity();

        if (result.IsSuccess)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            return await onSuccess(result.Value, cancellationToken).ConfigureAwait(false);
        }

        activity?.SetStatus(ActivityStatusCode.Error);

        var error = result.Error;

        return error switch
        {
            ValidationError ve when onValidation != null => await onValidation(ve, cancellationToken).ConfigureAwait(false),
            NotFoundError nf when onNotFound != null => await onNotFound(nf, cancellationToken).ConfigureAwait(false),
            ConflictError ce when onConflict != null => await onConflict(ce, cancellationToken).ConfigureAwait(false),
            BadRequestError br when onBadRequest != null => await onBadRequest(br, cancellationToken).ConfigureAwait(false),
            UnauthorizedError ua when onUnauthorized != null => await onUnauthorized(ua, cancellationToken).ConfigureAwait(false),
            ForbiddenError fe when onForbidden != null => await onForbidden(fe, cancellationToken).ConfigureAwait(false),
            DomainError de when onDomain != null => await onDomain(de, cancellationToken).ConfigureAwait(false),
            RateLimitError rl when onRateLimit != null => await onRateLimit(rl, cancellationToken).ConfigureAwait(false),
            ServiceUnavailableError su when onServiceUnavailable != null => await onServiceUnavailable(su, cancellationToken).ConfigureAwait(false),
            UnexpectedError ue when onUnexpected != null => await onUnexpected(ue, cancellationToken).ConfigureAwait(false),
            _ when onError != null => await onError(error, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                $"No handler provided for error type {error.GetType().Name}. " +
                "Either provide a specific handler or use onError as a catch-all.")
        };
    }

    /// <summary>
    /// Asynchronously pattern matches on the result with async handlers for different error types.
    /// </summary>
    /// <typeparam name="TIn">Type of the result value.</typeparam>
    /// <typeparam name="TOut">Type of the output.</typeparam>
    /// <param name="resultTask">The task representing the result to match on.</param>
    /// <param name="onSuccess">Async function to execute on success.</param>
    /// <param name="onValidation">Async function to execute when the error is a ValidationError.</param>
    /// <param name="onNotFound">Async function to execute when the error is a NotFoundError.</param>
    /// <param name="onConflict">Async function to execute when the error is a ConflictError.</param>
    /// <param name="onBadRequest">Async function to execute when the error is a BadRequestError.</param>
    /// <param name="onUnauthorized">Async function to execute when the error is an UnauthorizedError.</param>
    /// <param name="onForbidden">Async function to execute when the error is a ForbiddenError.</param>
    /// <param name="onDomain">Async function to execute when the error is a DomainError.</param>
    /// <param name="onRateLimit">Async function to execute when the error is a RateLimitError.</param>
    /// <param name="onServiceUnavailable">Async function to execute when the error is a ServiceUnavailableError.</param>
    /// <param name="onUnexpected">Async function to execute when the error is an UnexpectedError.</param>
    /// <param name="onError">Default async function to execute for any other error type.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the output from the appropriate handler function.</returns>
    public static async Task<TOut> MatchErrorAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, CancellationToken, Task<TOut>> onSuccess,
        Func<ValidationError, CancellationToken, Task<TOut>>? onValidation = null,
        Func<NotFoundError, CancellationToken, Task<TOut>>? onNotFound = null,
        Func<ConflictError, CancellationToken, Task<TOut>>? onConflict = null,
        Func<BadRequestError, CancellationToken, Task<TOut>>? onBadRequest = null,
        Func<UnauthorizedError, CancellationToken, Task<TOut>>? onUnauthorized = null,
        Func<ForbiddenError, CancellationToken, Task<TOut>>? onForbidden = null,
        Func<DomainError, CancellationToken, Task<TOut>>? onDomain = null,
        Func<RateLimitError, CancellationToken, Task<TOut>>? onRateLimit = null,
        Func<ServiceUnavailableError, CancellationToken, Task<TOut>>? onServiceUnavailable = null,
        Func<UnexpectedError, CancellationToken, Task<TOut>>? onUnexpected = null,
        Func<Error, CancellationToken, Task<TOut>>? onError = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(onSuccess);

        var result = await resultTask.ConfigureAwait(false);
        return await result.MatchErrorAsync(
            onSuccess,
            onValidation,
            onNotFound,
            onConflict,
            onBadRequest,
            onUnauthorized,
            onForbidden,
            onDomain,
            onRateLimit,
            onServiceUnavailable,
            onUnexpected,
            onError,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously executes different actions based on the result state and error type.
    /// </summary>
    /// <typeparam name="TIn">Type of the result value.</typeparam>
    /// <param name="resultTask">The task representing the result to switch on.</param>
    /// <param name="onSuccess">Async action to execute on success.</param>
    /// <param name="onValidation">Async action to execute when the error is a ValidationError.</param>
    /// <param name="onNotFound">Async action to execute when the error is a NotFoundError.</param>
    /// <param name="onConflict">Async action to execute when the error is a ConflictError.</param>
    /// <param name="onBadRequest">Async action to execute when the error is a BadRequestError.</param>
    /// <param name="onUnauthorized">Async action to execute when the error is an UnauthorizedError.</param>
    /// <param name="onForbidden">Async action to execute when the error is a ForbiddenError.</param>
    /// <param name="onDomain">Async action to execute when the error is a DomainError.</param>
    /// <param name="onRateLimit">Async action to execute when the error is a RateLimitError.</param>
    /// <param name="onServiceUnavailable">Async action to execute when the error is a ServiceUnavailableError.</param>
    /// <param name="onUnexpected">Async action to execute when the error is an UnexpectedError.</param>
    /// <param name="onError">Default async action to execute for any other error type.</param>
    /// <param name="cancellationToken">The cancellation token to observe.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static async Task SwitchErrorAsync<TIn>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, CancellationToken, Task> onSuccess,
        Func<ValidationError, CancellationToken, Task>? onValidation = null,
        Func<NotFoundError, CancellationToken, Task>? onNotFound = null,
        Func<ConflictError, CancellationToken, Task>? onConflict = null,
        Func<BadRequestError, CancellationToken, Task>? onBadRequest = null,
        Func<UnauthorizedError, CancellationToken, Task>? onUnauthorized = null,
        Func<ForbiddenError, CancellationToken, Task>? onForbidden = null,
        Func<DomainError, CancellationToken, Task>? onDomain = null,
        Func<RateLimitError, CancellationToken, Task>? onRateLimit = null,
        Func<ServiceUnavailableError, CancellationToken, Task>? onServiceUnavailable = null,
        Func<UnexpectedError, CancellationToken, Task>? onUnexpected = null,
        Func<Error, CancellationToken, Task>? onError = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        ArgumentNullException.ThrowIfNull(onSuccess);

        using var activity = RopTrace.ActivitySource.StartActivity();
        var result = await resultTask.ConfigureAwait(false);

        if (result.IsSuccess)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            await onSuccess(result.Value, cancellationToken).ConfigureAwait(false);
            return;
        }

        activity?.SetStatus(ActivityStatusCode.Error);

        var error = result.Error;

        switch (error)
        {
            case ValidationError ve when onValidation != null:
                await onValidation(ve, cancellationToken).ConfigureAwait(false);
                break;
            case NotFoundError nf when onNotFound != null:
                await onNotFound(nf, cancellationToken).ConfigureAwait(false);
                break;
            case ConflictError ce when onConflict != null:
                await onConflict(ce, cancellationToken).ConfigureAwait(false);
                break;
            case BadRequestError br when onBadRequest != null:
                await onBadRequest(br, cancellationToken).ConfigureAwait(false);
                break;
            case UnauthorizedError ua when onUnauthorized != null:
                await onUnauthorized(ua, cancellationToken).ConfigureAwait(false);
                break;
            case ForbiddenError fe when onForbidden != null:
                await onForbidden(fe, cancellationToken).ConfigureAwait(false);
                break;
            case DomainError de when onDomain != null:
                await onDomain(de, cancellationToken).ConfigureAwait(false);
                break;
            case RateLimitError rl when onRateLimit != null:
                await onRateLimit(rl, cancellationToken).ConfigureAwait(false);
                break;
            case ServiceUnavailableError su when onServiceUnavailable != null:
                await onServiceUnavailable(su, cancellationToken).ConfigureAwait(false);
                break;
            case UnexpectedError ue when onUnexpected != null:
                await onUnexpected(ue, cancellationToken).ConfigureAwait(false);
                break;
            default:
                if (onError != null)
                    await onError(error, cancellationToken).ConfigureAwait(false);
                else
                    throw new InvalidOperationException(
                        $"No handler provided for error type {error.GetType().Name}. " +
                        "Either provide a specific handler or use onError as a catch-all.");
                break;
        }
    }
}