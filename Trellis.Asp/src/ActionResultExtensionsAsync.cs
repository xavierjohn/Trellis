namespace Trellis.Asp;

using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using Trellis;

/// <summary>
/// Provides asynchronous extension methods to convert Task/ValueTask-wrapped Result types to ASP.NET Core ActionResult responses.
/// These methods enable clean async/await patterns in controllers while maintaining Railway Oriented Programming benefits.
/// </summary>
/// <remarks>
/// <para>
/// These extensions are async variants of <see cref="ActionResultExtensions"/>, designed for use with
/// async service methods. They support both <see cref="Task{TResult}"/> and <see cref="ValueTask{TResult}"/>
/// for maximum flexibility and performance.
/// </para>
/// <para>
/// Key benefits:
/// <list type="bullet">
/// <item>Clean async controller code without manual awaiting</item>
/// <item>Automatic HTTP status code selection based on error type</item>
/// <item>Support for both Task and ValueTask for performance optimization</item>
/// <item>Consistent error handling across async operations</item>
/// <item>Seamless integration with async Railway Oriented Programming chains</item>
/// </list>
/// </para>
/// <para>
/// Usage pattern in async controllers (with CancellationToken):
/// <code>
/// public class UsersController : ControllerBase
/// {
///     [HttpGet("{id}")]
///     public Task&lt;ActionResult&lt;UserDto&gt;&gt; GetUserAsync(string id, CancellationToken ct) =>
///         UserId.TryCreate(id)
///             .BindAsync(userId => _userService.GetUserAsync(userId, ct))
///             .MapAsync(user => new UserDto(user))
///             .ToActionResultAsync(this);
/// }
/// </code>
/// </para>
/// <para>
/// <strong>Best Practice:</strong> Always accept a <see cref="CancellationToken"/> parameter in async controller 
/// methods and pass it through to all async service calls. This enables proper request cancellation, 
/// graceful shutdown, and timeout handling.
/// </para>
/// </remarks>
public static class ActionResultExtensionsAsync
{

    /// <summary>
    /// Converts a Task-wrapped <see cref="Result{TValue}"/> to an <see cref="ActionResult{TValue}"/> with appropriate HTTP status code.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the result.</typeparam>
    /// <param name="resultTask">The task containing the result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
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
    /// This is the primary async method for converting domain results to HTTP responses.
    /// It awaits the result task and delegates to <see cref="ActionResultExtensions.ToActionResult{TValue}(Result{TValue}, ControllerBase)"/>.
    /// </para>
    /// <para>
    /// For performance-critical scenarios where the operation frequently completes synchronously,
    /// consider using the ValueTask overload instead.
    /// </para>
    /// <para>
    /// <strong>CancellationToken Best Practice:</strong> The controller method should accept a 
    /// <see cref="CancellationToken"/> parameter (ASP.NET Core automatically provides request cancellation)
    /// and pass it to all async service calls in the chain.
    /// </para>
    /// </remarks>
    /// <example>
    /// Async GET endpoint with database query and CancellationToken:
    /// <code>
    /// [HttpGet("{id}")]
    /// public Task&lt;ActionResult&lt;UserDto&gt;&gt; GetUserAsync(Guid id, CancellationToken ct) =>
    ///     UserId.TryCreate(id)
    ///         .BindAsync(userId => _repository.GetByIdAsync(userId, ct))
    ///         .MapAsync(user => new UserDto(user))
    ///         .ToActionResultAsync(this);
    /// 
    /// // Success: 200 OK with UserDto
    /// // Not found: 404 Not Found with Problem Details
    /// </code>
    /// </example>
    /// <example>
    /// Async POST endpoint with multiple operations and CancellationToken:
    /// <code>
    /// [HttpPost]
    /// public Task&lt;ActionResult&lt;OrderDto&gt;&gt; CreateOrderAsync(
    ///     CreateOrderRequest request,
    ///     CancellationToken ct) =>
    ///     CustomerId.TryCreate(request.CustomerId)
    ///         .BindAsync(customerId => _customerService.GetCustomerAsync(customerId, ct))
    ///         .BindAsync(customer => _orderService.CreateOrderAsync(customer, request.Items, ct))
    ///         .TapAsync(order => _eventBus.PublishAsync(new OrderCreatedEvent(order.Id), ct))
    ///         .MapAsync(order => new OrderDto(order))
    ///         .ToActionResultAsync(this);
    /// 
    /// // Success: 200 OK with OrderDto
    /// // Validation error: 400 Bad Request
    /// // Customer not found: 404 Not Found
    /// </code>
    /// </example>
    public static async Task<ActionResult<TValue>> ToActionResultAsync<TValue>(this Task<Result<TValue>> resultTask, ControllerBase controllerBase)
    {
        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return result.ToActionResult(controllerBase);
    }

    /// <summary>
    /// Converts a ValueTask-wrapped <see cref="Result{TValue}"/> to an <see cref="ActionResult{TValue}"/> with appropriate HTTP status code.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the result.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
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
    /// (e.g., cached results, in-memory operations). ValueTask can reduce allocations in these cases.
    /// </para>
    /// <para>
    /// Use this when your service methods return ValueTask for performance optimization.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [HttpGet("{id}")]
    /// public ValueTask&lt;ActionResult&lt;UserDto&gt;&gt; GetUserAsync(Guid id) =>
    ///     UserId.TryCreate(id)
    ///         .BindAsync(_cacheService.GetUserAsync) // Returns ValueTask
    ///         .MapAsync(user => new UserDto(user))
    ///         .ToActionResultAsync(this);
    /// </code>
    /// </example>
    public static async ValueTask<ActionResult<TValue>> ToActionResultAsync<TValue>(this ValueTask<Result<TValue>> resultTask, ControllerBase controllerBase)
    {
        Result<TValue> result = await resultTask.ConfigureAwait(false);
        return result.ToActionResult(controllerBase);
    }

    /// <summary>
    /// Converts a Task-wrapped <see cref="Result{TIn}"/> to an <see cref="ActionResult{TOut}"/> with support for partial content responses (Task variant).
    /// </summary>
    /// <typeparam name="TIn">The type of the value contained in the input result.</typeparam>
    /// <typeparam name="TOut">The type of the value in the output ActionResult.</typeparam>
    /// <param name="resultTask">The task containing the result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <param name="funcRange">Function that extracts <see cref="ContentRangeHeaderValue"/> from the input value.</param>
    /// <param name="funcValue">Function that transforms the input value to the output type.</param>
    /// <returns>
    /// A task that represents the asynchronous operation, containing:
    /// <list type="bullet">
    /// <item>206 Partial Content with Content-Range header if the range is a subset</item>
    /// <item>200 OK if the range represents the complete set</item>
    /// <item>Appropriate error status code if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// Async variant of <see cref="ActionResultExtensions.ToActionResult{TIn, TOut}(Result{TIn}, ControllerBase, Func{TIn, ContentRangeHeaderValue}, Func{TIn, TOut})"/>.
    /// Useful for async paginated queries.
    /// </remarks>
    /// <example>
    /// Async paginated endpoint:
    /// <code>
    /// [HttpGet]
    /// public Task&lt;ActionResult&lt;IEnumerable&lt;UserDto&gt;&gt;&gt; GetUsersAsync(
    ///     [FromQuery] int page = 0,
    ///     [FromQuery] int pageSize = 25) =>
    ///     _userService
    ///         .GetPagedUsersAsync(page, pageSize)
    ///         .ToActionResultAsync(
    ///             this,
    ///             funcRange: result => new ContentRangeHeaderValue(
    ///                 result.From, result.To, result.TotalCount),
    ///             funcValue: result => result.Items.Select(u => new UserDto(u))
    ///         );
    /// 
    /// // Returns 206 Partial Content with range headers for partial pages
    /// // Returns 200 OK when all items fit in response
    /// </code>
    /// </example>
    public static async Task<ActionResult<TOut>> ToActionResultAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        ControllerBase controllerBase,
        Func<TIn, ContentRangeHeaderValue> funcRange,
        Func<TIn, TOut> funcValue)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToActionResult(controllerBase, funcRange, funcValue);
    }

    /// <summary>
    /// Converts a ValueTask-wrapped <see cref="Result{TIn}"/> to an <see cref="ActionResult{TOut}"/> with support for partial content responses (ValueTask variant).
    /// </summary>
    /// <typeparam name="TIn">The type of the value contained in the input result.</typeparam>
    /// <typeparam name="TOut">The type of the value in the output ActionResult.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <param name="funcRange">Function that extracts <see cref="ContentRangeHeaderValue"/> from the input value.</param>
    /// <param name="funcValue">Function that transforms the input value to the output type.</param>
    /// <returns>
    /// A ValueTask that represents the asynchronous operation, containing:
    /// <list type="bullet">
    /// <item>206 Partial Content with Content-Range header if the range is a subset</item>
    /// <item>200 OK if the range represents the complete set</item>
    /// <item>Appropriate error status code if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// ValueTask variant optimized for scenarios with cached or frequently synchronous results.
    /// </remarks>
    public static async ValueTask<ActionResult<TOut>> ToActionResultAsync<TIn, TOut>(
    this ValueTask<Result<TIn>> resultTask,
    ControllerBase controllerBase,
    Func<TIn, ContentRangeHeaderValue> funcRange,
    Func<TIn, TOut> funcValue)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToActionResult(controllerBase, funcRange, funcValue);
    }

    /// <summary>
    /// Converts a Task-wrapped <see cref="Result{TValue}"/> to an <see cref="ActionResult{TValue}"/>
    /// that returns 206 Partial Content or 200 OK with scalar range parameters (Task variant).
    /// </summary>
    public static async Task<ActionResult<TValue>> ToActionResultAsync<TValue>(
        this Task<Result<TValue>> resultTask,
        ControllerBase controllerBase,
        long from,
        long to,
        long totalLength)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToActionResult(controllerBase, from, to, totalLength);
    }

    /// <summary>
    /// Converts a ValueTask-wrapped <see cref="Result{TValue}"/> to an <see cref="ActionResult{TValue}"/>
    /// that returns 206 Partial Content or 200 OK with scalar range parameters (ValueTask variant).
    /// </summary>
    public static async ValueTask<ActionResult<TValue>> ToActionResultAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask,
        ControllerBase controllerBase,
        long from,
        long to,
        long totalLength)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToActionResult(controllerBase, from, to, totalLength);
    }

    /// <summary>
    /// Converts a Task-wrapped <see cref="Result{TIn}"/> to an <see cref="ActionResult{TOut}"/>
    /// by applying a mapping function on success, or Problem Details on failure.
    /// </summary>
    /// <typeparam name="TIn">The type of the value contained in the input result.</typeparam>
    /// <typeparam name="TOut">The type of the value in the output ActionResult.</typeparam>
    /// <param name="resultTask">The task containing the result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <param name="map">Function that transforms the input value to the output type.</param>
    /// <returns>
    /// A task that represents the asynchronous operation, containing:
    /// <list type="bullet">
    /// <item>200 OK with transformed value if result is successful</item>
    /// <item>Appropriate error status code based on error type if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// Use this when the Mediator handler returns a domain type and the controller
    /// needs to map it to a DTO before returning.
    /// </remarks>
    /// <example>
    /// GET endpoint mapping domain to DTO:
    /// <code>
    /// [HttpGet("{id}")]
    /// public Task&lt;ActionResult&lt;OrderDto&gt;&gt; GetOrder(OrderId id) =>
    ///     _sender.Send(new GetOrderByIdQuery(id), ct)
    ///         .ToActionResultAsync(this, OrderDto.From);
    /// </code>
    /// </example>
    public static async Task<ActionResult<TOut>> ToActionResultAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        ControllerBase controllerBase,
        Func<TIn, TOut> map)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToActionResult(controllerBase, map);
    }

    /// <summary>
    /// Converts a ValueTask-wrapped <see cref="Result{TIn}"/> to an <see cref="ActionResult{TOut}"/>
    /// by applying a mapping function on success, or Problem Details on failure (ValueTask variant).
    /// </summary>
    /// <typeparam name="TIn">The type of the value contained in the input result.</typeparam>
    /// <typeparam name="TOut">The type of the value in the output ActionResult.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <param name="map">Function that transforms the input value to the output type.</param>
    /// <returns>
    /// A ValueTask that represents the asynchronous operation, containing:
    /// <list type="bullet">
    /// <item>200 OK with transformed value if result is successful</item>
    /// <item>Appropriate error status code based on error type if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// ValueTask variant optimized for scenarios with cached or frequently synchronous results.
    /// </remarks>
    public static async ValueTask<ActionResult<TOut>> ToActionResultAsync<TIn, TOut>(
        this ValueTask<Result<TIn>> resultTask,
        ControllerBase controllerBase,
        Func<TIn, TOut> map)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToActionResult(controllerBase, map);
    }

    /// <summary>
    /// Converts a Task-wrapped <see cref="Result{TValue}"/> to an <see cref="ActionResult{TValue}"/> that returns
    /// 201 Created with a Location header on success, or Problem Details on failure.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the result.</typeparam>
    /// <param name="resultTask">The task containing the result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <param name="actionName">The name of the action to use for generating the Location header URL.</param>
    /// <param name="routeValues">A function that extracts route values from the result value for URL generation.</param>
    /// <param name="controllerName">Optional controller name. When null, the current controller is used.</param>
    /// <returns>
    /// A task that represents the asynchronous operation, containing:
    /// <list type="bullet">
    /// <item>201 Created with Location header and value if result is successful</item>
    /// <item>Appropriate error status code (400-599) based on error type if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// Async variant of <see cref="ActionResultExtensions.ToCreatedAtActionResult{TValue}(Result{TValue}, ControllerBase, string, Func{TValue, object?}, string?)"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// [HttpPost]
    /// public Task&lt;ActionResult&lt;UserDto&gt;&gt; CreateUserAsync(
    ///     CreateUserRequest request, CancellationToken ct) =>
    ///     EmailAddress.TryCreate(request.Email)
    ///         .BindAsync(email => _userService.CreateUserAsync(email, ct))
    ///         .MapAsync(user => new UserDto(user))
    ///         .ToCreatedAtActionResultAsync(this,
    ///             actionName: nameof(GetUser),
    ///             routeValues: dto => new { id = dto.Id });
    /// </code>
    /// </example>
    public static async Task<ActionResult<TValue>> ToCreatedAtActionResultAsync<TValue>(
        this Task<Result<TValue>> resultTask,
        ControllerBase controllerBase,
        string actionName,
        Func<TValue, object?> routeValues,
        string? controllerName = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToCreatedAtActionResult(controllerBase, actionName, routeValues, controllerName);
    }

    /// <summary>
    /// Converts a ValueTask-wrapped <see cref="Result{TValue}"/> to an <see cref="ActionResult{TValue}"/> that returns
    /// 201 Created with a Location header on success, or Problem Details on failure.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the result.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <param name="actionName">The name of the action to use for generating the Location header URL.</param>
    /// <param name="routeValues">A function that extracts route values from the result value for URL generation.</param>
    /// <param name="controllerName">Optional controller name. When null, the current controller is used.</param>
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
    public static async ValueTask<ActionResult<TValue>> ToCreatedAtActionResultAsync<TValue>(
        this ValueTask<Result<TValue>> resultTask,
        ControllerBase controllerBase,
        string actionName,
        Func<TValue, object?> routeValues,
        string? controllerName = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToCreatedAtActionResult(controllerBase, actionName, routeValues, controllerName);
    }

    /// <summary>
    /// Converts a Task-wrapped <see cref="Result{TValue}"/> to an <see cref="ActionResult{TOut}"/> that returns
    /// 201 Created with a Location header on success (with value transformation), or Problem Details on failure.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the input result.</typeparam>
    /// <typeparam name="TOut">The type of the value in the output ActionResult.</typeparam>
    /// <param name="resultTask">The task containing the result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <param name="actionName">The name of the action to use for generating the Location header URL.</param>
    /// <param name="routeValues">A function that extracts route values from the result value for URL generation.</param>
    /// <param name="map">A function that transforms the input value to the output type for the response body.</param>
    /// <param name="controllerName">Optional controller name. When null, the current controller is used.</param>
    /// <returns>
    /// A task that represents the asynchronous operation, containing:
    /// <list type="bullet">
    /// <item>201 Created with Location header and mapped value if result is successful</item>
    /// <item>Appropriate error status code (400-599) based on error type if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// Async variant of <see cref="ActionResultExtensions.ToCreatedAtActionResult{TValue, TOut}(Result{TValue}, ControllerBase, string, Func{TValue, object?}, Func{TValue, TOut}, string?)"/>.
    /// </remarks>
    /// <example>
    /// <code>
    /// [HttpPost]
    /// public Task&lt;ActionResult&lt;UserDto&gt;&gt; CreateUserAsync(
    ///     CreateUserRequest request, CancellationToken ct) =>
    ///     EmailAddress.TryCreate(request.Email)
    ///         .BindAsync(email => _userService.CreateUserAsync(email, ct))
    ///         .ToCreatedAtActionResultAsync(this,
    ///             actionName: nameof(GetUser),
    ///             routeValues: user => new { id = user.Id },
    ///             map: user => new UserDto(user));
    /// </code>
    /// </example>
    public static async Task<ActionResult<TOut>> ToCreatedAtActionResultAsync<TValue, TOut>(
        this Task<Result<TValue>> resultTask,
        ControllerBase controllerBase,
        string actionName,
        Func<TValue, object?> routeValues,
        Func<TValue, TOut> map,
        string? controllerName = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToCreatedAtActionResult(controllerBase, actionName, routeValues, map, controllerName);
    }

    /// <summary>
    /// Converts a ValueTask-wrapped <see cref="Result{TValue}"/> to an <see cref="ActionResult{TOut}"/> that returns
    /// 201 Created with a Location header on success (with value transformation), or Problem Details on failure.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the input result.</typeparam>
    /// <typeparam name="TOut">The type of the value in the output ActionResult.</typeparam>
    /// <param name="resultTask">The ValueTask containing the result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <param name="actionName">The name of the action to use for generating the Location header URL.</param>
    /// <param name="routeValues">A function that extracts route values from the result value for URL generation.</param>
    /// <param name="map">A function that transforms the input value to the output type for the response body.</param>
    /// <param name="controllerName">Optional controller name. When null, the current controller is used.</param>
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
    public static async ValueTask<ActionResult<TOut>> ToCreatedAtActionResultAsync<TValue, TOut>(
        this ValueTask<Result<TValue>> resultTask,
        ControllerBase controllerBase,
        string actionName,
        Func<TValue, object?> routeValues,
        Func<TValue, TOut> map,
        string? controllerName = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToCreatedAtActionResult(controllerBase, actionName, routeValues, map, controllerName);
    }

    /// <summary>
    /// Converts a Task-wrapped <see cref="Result{TIn}"/> to an <see cref="ActionResult{TOut}"/> that returns
    /// 201 Created with a Location header and representation metadata headers on success, or Problem Details on failure (Task variant).
    /// </summary>
    public static async Task<ActionResult<TOut>> ToCreatedAtActionResultAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        ControllerBase controllerBase,
        string actionName,
        Func<TIn, object?> routeValues,
        Func<TIn, RepresentationMetadata> metadataSelector,
        Func<TIn, TOut> map,
        string? controllerName = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToCreatedAtActionResult(controllerBase, actionName, routeValues, metadataSelector, map, controllerName);
    }

    /// <summary>
    /// Converts a ValueTask-wrapped <see cref="Result{TIn}"/> to an <see cref="ActionResult{TOut}"/> that returns
    /// 201 Created with a Location header and representation metadata headers on success, or Problem Details on failure (ValueTask variant).
    /// </summary>
    public static async ValueTask<ActionResult<TOut>> ToCreatedAtActionResultAsync<TIn, TOut>(
        this ValueTask<Result<TIn>> resultTask,
        ControllerBase controllerBase,
        string actionName,
        Func<TIn, object?> routeValues,
        Func<TIn, RepresentationMetadata> metadataSelector,
        Func<TIn, TOut> map,
        string? controllerName = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToCreatedAtActionResult(controllerBase, actionName, routeValues, metadataSelector, map, controllerName);
    }

}