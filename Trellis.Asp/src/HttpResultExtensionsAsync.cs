namespace Trellis.Asp;

using Microsoft.AspNetCore.Http;
using Trellis;

/// <summary>
/// Provides asynchronous extension methods to convert Task/ValueTask-wrapped Result types to ASP.NET Core Minimal API <see cref="Microsoft.AspNetCore.Http.IResult"/> responses.
/// These methods enable clean async patterns in Minimal API endpoints while maintaining Railway Oriented Programming benefits.
/// </summary>
/// <remarks>
/// <para>
/// These extensions are async variants of <see cref="HttpResultExtensions"/>, designed for use with
/// async service methods in Minimal APIs. They support both <see cref="Task{TResult}"/> and <see cref="ValueTask{TResult}"/>
/// for maximum flexibility and performance.
/// </para>
/// <para>
/// Key benefits:
/// <list type="bullet">
/// <item>Clean async Minimal API endpoint code without manual awaiting</item>
/// <item>Automatic HTTP status code selection based on error type</item>
/// <item>Support for both Task and ValueTask for performance optimization</item>
/// <item>Consistent error handling across async operations</item>
/// <item>Seamless integration with async Railway Oriented Programming chains</item>
/// <item>Reduced boilerplate in endpoint definitions</item>
/// </list>
/// </para>
/// <para>
/// Usage pattern in async Minimal APIs (with CancellationToken):
/// <code>
/// app.MapGet("/users/{id}", async (string id, IUserService userService, CancellationToken ct) =>
///     await UserId.TryCreate(id)
///         .BindAsync(userId => userService.GetUserAsync(userId, ct))
///         .MapAsync(user => new UserDto(user))
///         .ToHttpResultAsync()
/// );
/// </code>
/// </para>
/// <para>
/// <strong>Best Practice:</strong> Always accept a <see cref="CancellationToken"/> parameter in async endpoints
/// and pass it through to all async service calls. ASP.NET Core automatically provides request cancellation
/// through this token, enabling proper timeout handling and graceful shutdown.
/// </para>
/// </remarks>
public static class HttpResultExtensionsAsync
{

    /// <summary>
    /// Converts a Task-wrapped <see cref="Result{TValue}"/> to an <see cref="Microsoft.AspNetCore.Http.IResult"/> with appropriate HTTP status code.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the result.</typeparam>
    /// <param name="resultTask">The task containing the result object to convert.</param>
    /// <param name="options">Optional custom error-to-status-code mappings. When null, uses default mappings.</param>
    /// <returns>
    /// A task that represents the asynchronous operation, containing:
    /// <list type="bullet">
    /// <item>200 OK with value if result is successful (except for Unit)</item>
    /// <item>204 No Content if result is successful and TValue is Unit</item>
    /// <item>Appropriate error status code (400-599) based on error type if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the primary async method for converting domain results to HTTP responses in Minimal APIs.
    /// It awaits the result task and delegates to <see cref="HttpResultExtensions.ToHttpResult{TValue}(Result{TValue}, TrellisAspOptions)"/>.
    /// </para>
    /// <para>
    /// For performance-critical scenarios where the operation frequently completes synchronously,
    /// consider using the ValueTask overload instead.
    /// </para>
    /// <para>
    /// <strong>CancellationToken Best Practice:</strong> The endpoint should accept a 
    /// <see cref="CancellationToken"/> parameter (ASP.NET Core automatically provides request cancellation)
    /// and pass it to all async service calls in the chain.
    /// </para>
    /// </remarks>
    /// <example>
    /// Async GET endpoint with database query and CancellationToken:
    /// <code>
    /// app.MapGet("/users/{id}", async (Guid id, IUserRepository repository, CancellationToken ct) =>
    ///     await UserId.TryCreate(id)
    ///         .BindAsync(userId => repository.GetByIdAsync(userId, ct))
    ///         .MapAsync(user => new UserDto(user))
    ///         .ToHttpResultAsync());
    /// 
    /// // Success: 200 OK with UserDto
    /// // Not found: 404 Not Found with Problem Details
    /// // Validation error: 400 Bad Request
    /// </code>
    /// </example>
    /// <example>
    /// Async POST endpoint with multiple operations and CancellationToken:
    /// <code>
    /// app.MapPost("/orders", async (
    ///     CreateOrderRequest request,
    ///     IOrderService orderService,
    ///     IEventBus eventBus,
    ///     CancellationToken ct) =>
    ///     await CustomerId.TryCreate(request.CustomerId)
    ///         .BindAsync(customerId => orderService.GetCustomerAsync(customerId, ct))
    ///         .BindAsync(customer => orderService.CreateOrderAsync(customer, request.Items, ct))
    ///         .TapAsync(order => eventBus.PublishAsync(new OrderCreatedEvent(order.Id), ct))
    ///         .MapAsync(order => new OrderDto(order))
    ///         .ToHttpResultAsync());
    /// 
    /// // Success: 200 OK with OrderDto
    /// // Validation error: 400 Bad Request
    /// // Customer not found: 404 Not Found
    /// // Domain error: 422 Unprocessable Entity
    /// </code>
    /// </example>
    /// <example>
    /// Async DELETE endpoint returning Unit with CancellationToken:
    /// <code>
    /// app.MapDelete("/users/{id}", async (Guid id, IUserRepository repository, CancellationToken ct) =>
    ///     await UserId.TryCreate(id)
    ///         .BindAsync(userId => repository.DeleteAsync(userId, ct))
    ///         .ToHttpResultAsync());
    /// 
    /// // Success: 204 No Content (automatic for Unit)
    /// // Not found: 404 Not Found
    /// </code>
    /// </example>
    /// <example>
    /// Complex async workflow with validation and side effects:
    /// <code>
    /// app.MapPost("/payments", async (
    ///     ProcessPaymentRequest request,
    ///     IPaymentService paymentService,
    ///     INotificationService notificationService,
    ///     CancellationToken ct) =>
    ///     await Amount.TryCreate(request.Amount)
    ///         .Combine(CardNumber.TryCreate(request.CardNumber))
    ///         .BindAsync((amount, card) => 
    ///             paymentService.ProcessPaymentAsync(amount, card, ct))
    ///         .TapAsync(payment => 
    ///             notificationService.SendReceiptAsync(payment, ct))
    ///         .MapAsync(payment => new PaymentDto(payment))
    ///         .ToHttpResultAsync());
    /// 
    /// // Returns appropriate status codes for validation errors,
    /// // payment failures, or successful processing
    /// </code>
    /// </example>
    public static async Task<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TValue>(this Task<Result<TValue>> resultTask, TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToHttpResult(options);
    }

    /// <summary>
    /// Converts a ValueTask-wrapped <see cref="Result{TValue}"/> to an <see cref="Microsoft.AspNetCore.Http.IResult"/> with appropriate HTTP status code.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the result.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result object to convert.</param>
    /// <param name="options">Optional custom error-to-status-code mappings. When null, uses default mappings.</param>
    /// <returns>
    /// A ValueTask that represents the asynchronous operation, containing:
    /// <list type="bullet">
    /// <item>200 OK with value if result is successful (except for Unit)</item>
    /// <item>204 No Content if result is successful and TValue is Unit</item>
    /// <item>Appropriate error status code based on error type if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// This overload is optimized for scenarios where the async operation frequently completes synchronously
    /// (e.g., cached results, in-memory operations, ValueTask-returning service methods).
    /// ValueTask can reduce allocations in these cases.
    /// </para>
    /// <para>
    /// Use this when your service methods return ValueTask for performance optimization.
    /// </para>
    /// </remarks>
    /// <example>
    /// Using with cached data that might complete synchronously:
    /// <code>
    /// app.MapGet("/users/{id}", async (Guid id, IUserCache cache, CancellationToken ct) =>
    ///     await UserId.TryCreate(id)
    ///         .BindAsync(userId => cache.GetUserAsync(userId, ct)) // Returns ValueTask
    ///         .MapAsync(user => new UserDto(user))
    ///         .ToHttpResultAsync());
    /// 
    /// // Optimized for frequent cache hits that complete synchronously
    /// </code>
    /// </example>
    /// <example>
    /// High-performance endpoint with ValueTask throughout:
    /// <code>
    /// app.MapGet("/metrics/{id}", async (string id, IMetricsService service, CancellationToken ct) =>
    ///     await MetricId.TryCreate(id)
    ///         .BindAsync(metricId => service.GetMetricAsync(metricId, ct)) // ValueTask
    ///         .MapAsync(metric => new MetricDto(metric))
    ///         .ToHttpResultAsync());
    /// 
    /// // Reduced allocations for high-throughput scenarios
    /// </code>
    /// </example>
    public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TValue>(this ValueTask<Result<TValue>> resultTask, TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToHttpResult(options);
    }

    /// <summary>
    /// Converts a Task-wrapped <see cref="Result{TValue}"/> to an <see cref="Microsoft.AspNetCore.Http.IResult"/> that returns
    /// 201 Created with a Location header on success, or Problem Details on failure.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the result.</typeparam>
    /// <param name="resultTask">The task containing the result object to convert.</param>
    /// <param name="routeName">The name of the route to use for generating the Location header URL.</param>
    /// <param name="routeValues">A function that extracts route values from the result value for URL generation.
    /// Return a <see cref="Microsoft.AspNetCore.Routing.RouteValueDictionary"/> to remain AOT-compatible.</param>
    /// <param name="options">Optional custom error-to-status-code mappings. When null, uses default mappings.</param>
    /// <returns>
    /// A task that represents the asynchronous operation, containing:
    /// <list type="bullet">
    /// <item>201 Created with Location header and value if result is successful</item>
    /// <item>Appropriate error status code (400-599) based on error type if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// Async variant of <see cref="HttpResultExtensions.ToCreatedAtRouteHttpResult{TValue}"/>.
    /// </remarks>
    public static async Task<Microsoft.AspNetCore.Http.IResult> ToCreatedAtRouteHttpResultAsync<TValue>(
        this Task<Result<TValue>> resultTask,
        string routeName,
        Func<TValue, Microsoft.AspNetCore.Routing.RouteValueDictionary> routeValues,
        TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToCreatedAtRouteHttpResult(routeName, routeValues, options);
    }

    /// <summary>
    /// Converts a ValueTask-wrapped <see cref="Result{TValue}"/> to an <see cref="Microsoft.AspNetCore.Http.IResult"/> that returns
    /// 201 Created with a Location header on success, or Problem Details on failure.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the result.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result object to convert.</param>
    /// <param name="routeName">The name of the route to use for generating the Location header URL.</param>
    /// <param name="routeValues">A function that extracts route values from the result value for URL generation.</param>
    /// <param name="options">Optional custom error-to-status-code mappings. When null, uses default mappings.</param>
    /// <returns>
    /// A ValueTask that represents the asynchronous operation, containing:
    /// <list type="bullet">
    /// <item>201 Created with Location header and value if result is successful</item>
    /// <item>Appropriate error status code (400-599) based on error type if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// ValueTask variant optimized for scenarios with cached or frequently synchronous results.
    /// </remarks>
    public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToCreatedAtRouteHttpResultAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask,
        string routeName,
        Func<TValue, Microsoft.AspNetCore.Routing.RouteValueDictionary> routeValues,
        TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToCreatedAtRouteHttpResult(routeName, routeValues, options);
    }

    /// <summary>
    /// Converts a Task-wrapped <see cref="Result{TValue}"/> to an <see cref="Microsoft.AspNetCore.Http.IResult"/> that returns
    /// 201 Created with a Location header on success (with value transformation), or Problem Details on failure.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the input result.</typeparam>
    /// <typeparam name="TOut">The type of the value in the response body.</typeparam>
    /// <param name="resultTask">The task containing the result object to convert.</param>
    /// <param name="routeName">The name of the route to use for generating the Location header URL.</param>
    /// <param name="routeValues">A function that extracts route values from the result value for URL generation.</param>
    /// <param name="map">A function that transforms the input value to the output type for the response body.</param>
    /// <param name="options">Optional custom error-to-status-code mappings. When null, uses default mappings.</param>
    /// <returns>
    /// A task that represents the asynchronous operation, containing:
    /// <list type="bullet">
    /// <item>201 Created with Location header and mapped value if result is successful</item>
    /// <item>Appropriate error status code (400-599) based on error type if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// Async variant of <see cref="HttpResultExtensions.ToCreatedAtRouteHttpResult{TValue, TOut}"/>.
    /// </remarks>
    public static async Task<Microsoft.AspNetCore.Http.IResult> ToCreatedAtRouteHttpResultAsync<TValue, TOut>(
        this Task<Result<TValue>> resultTask,
        string routeName,
        Func<TValue, Microsoft.AspNetCore.Routing.RouteValueDictionary> routeValues,
        Func<TValue, TOut> map,
        TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToCreatedAtRouteHttpResult(routeName, routeValues, map, options);
    }

    /// <summary>
    /// Converts a ValueTask-wrapped <see cref="Result{TValue}"/> to an <see cref="Microsoft.AspNetCore.Http.IResult"/> that returns
    /// 201 Created with a Location header on success (with value transformation), or Problem Details on failure.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the input result.</typeparam>
    /// <typeparam name="TOut">The type of the value in the response body.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result object to convert.</param>
    /// <param name="routeName">The name of the route to use for generating the Location header URL.</param>
    /// <param name="routeValues">A function that extracts route values from the result value for URL generation.</param>
    /// <param name="map">A function that transforms the input value to the output type for the response body.</param>
    /// <param name="options">Optional custom error-to-status-code mappings. When null, uses default mappings.</param>
    /// <returns>
    /// A ValueTask that represents the asynchronous operation, containing:
    /// <list type="bullet">
    /// <item>201 Created with Location header and mapped value if result is successful</item>
    /// <item>Appropriate error status code (400-599) based on error type if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// ValueTask variant optimized for scenarios with cached or frequently synchronous results.
    /// </remarks>
    public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToCreatedAtRouteHttpResultAsync<TValue, TOut>(
        this ValueTask<Result<TValue>> resultTask,
        string routeName,
        Func<TValue, Microsoft.AspNetCore.Routing.RouteValueDictionary> routeValues,
        Func<TValue, TOut> map,
        TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToCreatedAtRouteHttpResult(routeName, routeValues, map, options);
    }

    #region RFC 9110 — ETag / If-None-Match Async Support (Minimal API)

    /// <summary>
    /// Async Task overload: converts result to Minimal API IResult with ETag header and If-None-Match (304) support.
    /// </summary>
    public static async Task<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        HttpContext httpContext,
        Func<TIn, string> etagSelector,
        Func<TIn, TOut> map,
        TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToHttpResult(httpContext, etagSelector, map, options);
    }

    /// <summary>
    /// Async ValueTask overload: converts result to Minimal API IResult with ETag header and If-None-Match (304) support.
    /// </summary>
    public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TIn, TOut>(
        this ValueTask<Result<TIn>> resultTask,
        HttpContext httpContext,
        Func<TIn, string> etagSelector,
        Func<TIn, TOut> map,
        TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToHttpResult(httpContext, etagSelector, map, options);
    }

    /// <summary>
    /// Async Task overload: converts result to 201 Created Minimal API IResult with ETag header.
    /// </summary>
    public static async Task<Microsoft.AspNetCore.Http.IResult> ToCreatedHttpResultAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        HttpContext httpContext,
        Func<TIn, string> uriSelector,
        Func<TIn, string> etagSelector,
        Func<TIn, TOut> map,
        TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToCreatedHttpResult(httpContext, uriSelector, etagSelector, map, options);
    }

    /// <summary>
    /// Async ValueTask overload: converts result to 201 Created Minimal API IResult with ETag header.
    /// </summary>
    public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToCreatedHttpResultAsync<TIn, TOut>(
        this ValueTask<Result<TIn>> resultTask,
        HttpContext httpContext,
        Func<TIn, string> uriSelector,
        Func<TIn, string> etagSelector,
        Func<TIn, TOut> map,
        TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToCreatedHttpResult(httpContext, uriSelector, etagSelector, map, options);
    }

    #endregion

    #region RFC 9110 — Partial Content / Pagination Async (Minimal API)

    /// <summary>
    /// Async Task overload: converts result to 206 Partial Content or 200 OK with range parameters.
    /// </summary>
    public static async Task<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TValue>(
        this Task<Result<TValue>> resultTask,
        long from,
        long to,
        long totalLength,
        TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToHttpResult(from, to, totalLength, options);
    }

    /// <summary>
    /// Async ValueTask overload: converts result to 206 Partial Content or 200 OK with range parameters.
    /// </summary>
    public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask,
        long from,
        long to,
        long totalLength,
        TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToHttpResult(from, to, totalLength, options);
    }

    /// <summary>
    /// Async Task overload: converts result to 206 Partial Content or 200 OK with lambda-based Content-Range.
    /// </summary>
    public static async Task<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, System.Net.Http.Headers.ContentRangeHeaderValue> funcRange,
        Func<TIn, TOut> funcValue,
        TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToHttpResult(funcRange, funcValue, options);
    }

    /// <summary>
    /// Async ValueTask overload: converts result to 206 Partial Content or 200 OK with lambda-based Content-Range.
    /// </summary>
    public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TIn, TOut>(
        this ValueTask<Result<TIn>> resultTask,
        Func<TIn, System.Net.Http.Headers.ContentRangeHeaderValue> funcRange,
        Func<TIn, TOut> funcValue,
        TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToHttpResult(funcRange, funcValue, options);
    }

    #endregion

    #region RFC 7240 — ToUpdatedHttpResult Async (Minimal API)

    /// <summary>
    /// Async Task overload: converts result to an updated Minimal API response with Prefer support.
    /// </summary>
    public static async Task<Microsoft.AspNetCore.Http.IResult> ToUpdatedHttpResultAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        HttpContext httpContext,
        RepresentationMetadata? metadata,
        Func<TIn, TOut> map,
        TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToUpdatedHttpResult(httpContext, metadata, map, options);
    }

    /// <summary>
    /// Async ValueTask overload: converts result to an updated Minimal API response with Prefer support.
    /// </summary>
    public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToUpdatedHttpResultAsync<TIn, TOut>(
        this ValueTask<Result<TIn>> resultTask,
        HttpContext httpContext,
        RepresentationMetadata? metadata,
        Func<TIn, TOut> map,
        TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToUpdatedHttpResult(httpContext, metadata, map, options);
    }

    /// <summary>
    /// Async Task overload: converts result to an updated Minimal API response with dynamic metadata and Prefer support.
    /// </summary>
    public static async Task<Microsoft.AspNetCore.Http.IResult> ToUpdatedHttpResultAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        HttpContext httpContext,
        Func<TIn, RepresentationMetadata> metadataSelector,
        Func<TIn, TOut> map,
        TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToUpdatedHttpResult(httpContext, metadataSelector, map, options);
    }

    /// <summary>
    /// Async ValueTask overload: converts result to an updated Minimal API response with dynamic metadata and Prefer support.
    /// </summary>
    public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToUpdatedHttpResultAsync<TIn, TOut>(
        this ValueTask<Result<TIn>> resultTask,
        HttpContext httpContext,
        Func<TIn, RepresentationMetadata> metadataSelector,
        Func<TIn, TOut> map,
        TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToUpdatedHttpResult(httpContext, metadataSelector, map, options);
    }

    #endregion
}