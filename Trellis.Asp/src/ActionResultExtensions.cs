namespace Trellis.Asp;

using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Trellis;

/// <summary>
/// Provides extension methods to convert Result types to ASP.NET Core ActionResult responses.
/// These methods bridge Railway Oriented Programming with ASP.NET Core MVC/Web API controllers.
/// </summary>
/// <remarks>
/// <para>
/// These extensions enable clean, declarative controller code by automatically mapping
/// domain Result types to appropriate HTTP responses. They handle:
/// <list type="bullet">
/// <item>Automatic HTTP status code selection based on error type</item>
/// <item>Problem Details (RFC 9457) formatting for errors</item>
/// <item>Validation error formatting with ModelState</item>
/// <item>Partial content (206) responses with range headers</item>
/// <item>Unit result to 204 No Content conversion</item>
/// </list>
/// </para>
/// <para>
/// Usage pattern in controllers:
/// <code>
/// public class UsersController : ControllerBase
/// {
///     [HttpGet("{id}")]
///     public ActionResult&lt;UserDto&gt; GetUser(string id) =>
///         UserId.TryCreate(id)
///             .Bind(_userService.GetUser)
///             .Map(user => new UserDto(user))
///             .ToActionResult(this);
/// }
/// </code>
/// </para>
/// </remarks>
public static class ActionResultExtensions
{
    /// <summary>
    /// Converts a <see cref="Result{TValue}"/> to an <see cref="ActionResult{TValue}"/> with appropriate HTTP status code.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the result.</typeparam>
    /// <param name="result">The result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>200 OK with value if result is successful (except for Unit)</item>
    /// <item>204 No Content if result is successful and TValue is Unit</item>
    /// <item>Appropriate error status code (400-599) based on error type if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the primary method for converting domain results to HTTP responses in controllers.
    /// It automatically selects the appropriate status code based on the error type.
    /// </para>
    /// <para>
    /// Special handling for <see cref="Unit"/>: Since Unit represents "no value", 
    /// successful Unit results return 204 No Content instead of 200 OK.
    /// This is appropriate for operations like DELETE or state-changing operations
    /// that don't return data.
    /// </para>
    /// </remarks>
    /// <example>
    /// Simple GET endpoint:
    /// <code>
    /// [HttpGet("{id}")]
    /// public ActionResult&lt;UserDto&gt; GetUser(Guid id) =>
    ///     UserId.TryCreate(id)
    ///         .Bind(_repository.GetAsync)
    ///         .Map(user => new UserDto(user))
    ///         .ToActionResult(this);
    /// 
    /// // Success: 200 OK with UserDto
    /// // Not found: 404 Not Found with Problem Details
    /// // Validation error: 400 Bad Request with validation details
    /// </code>
    /// </example>
    /// <example>
    /// POST endpoint returning 201 Created with Location header:
    /// <code>
    /// [HttpPost]
    /// public ActionResult&lt;UserDto&gt; CreateUser(CreateUserRequest request) =>
    ///     EmailAddress.TryCreate(request.Email)
    ///         .Combine(FirstName.TryCreate(request.FirstName))
    ///         .Bind((email, name) => _userService.CreateUser(email, name))
    ///         .ToCreatedAtActionResult(this,
    ///             actionName: nameof(GetUser),
    ///             routeValues: user => new { id = user.Id },
    ///             map: user => new UserDto(user));
    /// 
    /// // Success: 201 Created with Location: /api/users/{id}
    /// // Validation error: 400 Bad Request with field-level errors
    /// // Conflict: 409 Conflict if user already exists
    /// </code>
    /// </example>
    /// <example>
    /// DELETE endpoint returning a non-generic <see cref="Result"/>:
    /// <code>
    /// [HttpDelete("{id}")]
    /// public ActionResult DeleteUser(Guid id) =>
    ///     UserId.TryCreate(id)
    ///         .Bind(_repository.DeleteAsync)
    ///         .ToActionResult(this);
    /// 
    /// // Success: 204 No Content (automatic for non-generic Result)
    /// // Not found: 404 Not Found
    /// </code>
    /// </example>
    public static ActionResult<TValue> ToActionResult<TValue>(this Result<TValue> result, ControllerBase controllerBase) =>
        result.Match<TValue, ActionResult<TValue>>(
            onSuccess: value => (ActionResult<TValue>)controllerBase.Ok(value),
            onFailure: error => error.ToActionResult<TValue>(controllerBase));

    /// <summary>
    /// Converts a non-generic <see cref="Result"/> to an <see cref="ActionResult"/>.
    /// On success returns 204 No Content; on failure returns an error result.
    /// </summary>
    public static ActionResult ToActionResult(this Result result, ControllerBase controllerBase) =>
        result.Match(
            onSuccess: () => (ActionResult)controllerBase.NoContent(),
            onFailure: error => error.ToActionResult(controllerBase));

    /// <summary>
    /// Converts a domain <see cref="Error"/> directly to a non-generic <see cref="ActionResult"/> with appropriate HTTP status code and Problem Details format.
    /// </summary>
    public static ActionResult ToActionResult(this Error error, ControllerBase controllerBase) =>
        error.ToActionResult<object>(controllerBase).Result!;

    /// <summary>
    /// Converts a domain <see cref="Error"/> to an <see cref="ActionResult{TValue}"/> with appropriate HTTP status code and Problem Details format.
    /// </summary>
    /// <typeparam name="TValue">The type of the ActionResult value.</typeparam>
    /// <param name="error">The domain error to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <returns>
    /// An ActionResult with Problem Details (RFC 9457) response. The HTTP status code is resolved
    /// from <see cref="TrellisAspOptions"/> (configured via <c>AddTrellisAsp</c>). The default mappings are:
    /// <list type="table">
    ///     <listheader>
    ///         <term>Domain Error Type</term>
    ///         <description>Default HTTP Status Code</description>
    ///     </listheader>
    ///     <item>
    ///         <term><see cref="Error.UnprocessableContent"/></term>
    ///         <description>422 Unprocessable Content with validation problem details and ModelState</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="Error.BadRequest"/></term>
    ///         <description>400 Bad Request</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="Error.Unauthorized"/></term>
    ///         <description>401 Unauthorized</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="Error.Forbidden"/></term>
    ///         <description>403 Forbidden</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="Error.NotFound"/></term>
    ///         <description>404 Not Found</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="Error.Conflict"/></term>
    ///         <description>409 Conflict</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="Error.InternalServerError"/></term>
    ///         <description>500 Internal Server Error</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="Error.TooManyRequests"/></term>
    ///         <description>429 Too Many Requests</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="Error.InternalServerError"/></term>
    ///         <description>500 Internal Server Error</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="Error.ServiceUnavailable"/></term>
    ///         <description>503 Service Unavailable</description>
    ///     </item>
    ///     <item>
    ///         <term>Unknown types</term>
    ///         <description>500 Internal Server Error</description>
    ///     </item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// Status codes are resolved via <see cref="TrellisAspOptions"/>, which is configured by
    /// calling <c>AddTrellisAsp</c> at startup. Any mapping can be overridden:
    /// <code>
    /// builder.Services.AddTrellisAsp(options =>
    /// {
    ///     options.MapError&lt;Error.Conflict&gt;(StatusCodes.Status400BadRequest);
    /// });
    /// </code>
    /// If <c>AddTrellisAsp</c> is not called, the default mappings shown above are used.
    /// </para>
    /// <para>
    /// All responses use Problem Details format (RFC 9457) which provides a standard way to
    /// communicate errors in HTTP APIs. The format includes:
    /// <list type="bullet">
    /// <item><c>type</c>: A URI reference identifying the problem type</item>
    /// <item><c>title</c>: A short human-readable summary</item>
    /// <item><c>status</c>: The HTTP status code</item>
    /// <item><c>detail</c>: A human-readable explanation (from error.Detail)</item>
    /// <item><c>instance</c>: A URI reference identifying the specific occurrence (from error.Instance)</item>
    /// </list>
    /// </para>
    /// <para>
    /// For <see cref="Error.UnprocessableContent"/>, the response includes an additional <c>errors</c> object
    /// containing field-level validation messages compatible with ASP.NET Core ModelState.
    /// </para>
    /// </remarks>
    /// <example>
    /// Example Problem Details response for a validation error:
    /// <code>
    /// {
    ///   "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
    ///   "title": "One or more validation errors occurred.",
    ///   "status": 400,
    ///   "detail": "User data validation failed",
    ///   "instance": "/api/users",
    ///   "errors": {
    ///     "email": ["Email address is invalid"],
    ///     "age": ["Age must be 18 or older"]
    ///   }
    /// }
    /// </code>
    /// </example>
    public static ActionResult<TValue> ToActionResult<TValue>(this Error error, ControllerBase controllerBase)
    {
        var options = controllerBase.HttpContext?.RequestServices?.GetService<TrellisAspOptions>()
                      ?? TrellisAspOptions.Default;
        var statusCode = options.GetStatusCode(error);

        if (error is Error.UnprocessableContent validation
            && (validation.Fields.Items.Length > 0 || validation.Rules.Items.Length > 0))
            return ValidationErrors<TValue>(string.IsNullOrEmpty(error.Detail) ? null : error.Detail, validation, instance: null, controllerBase, statusCode);

        if (controllerBase.HttpContext is not null)
            EmitCompanionHeaders(error, controllerBase.Response);

        var detail = statusCode >= 500 ? "An internal error occurred." : error.Detail;
        var problem = controllerBase.Problem(detail, instance: null, statusCode);
        ApplyExtensions(problem, error, error is Error.UnprocessableContent uc ? uc.Rules : default);
        return (ActionResult<TValue>)problem;
    }

    /// <summary>
    /// Converts a <see cref="Result{TIn}"/> to an <see cref="ActionResult{TOut}"/> with support for partial content responses,
    /// using custom functions to extract range information and transform the value.
    /// </summary>
    /// <typeparam name="TIn">The type of the value contained in the input result.</typeparam>
    /// <typeparam name="TOut">The type of the value in the output ActionResult.</typeparam>
    /// <param name="result">The result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <param name="funcRange">Function that extracts <see cref="ContentRangeHeaderValue"/> from the input value.</param>
    /// <param name="funcValue">Function that transforms the input value to the output type.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>206 Partial Content with Content-Range header if the range is a subset</item>
    /// <item>200 OK if the range represents the complete set</item>
    /// <item>Appropriate error status code if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// This overload is useful when the result value contains both the data and range information,
    /// and you need to transform the value before returning it.
    /// </para>
    /// <para>
    /// Common scenarios:
    /// <list type="bullet">
    /// <item>Returning a subset of properties from a complex result object</item>
    /// <item>Mapping domain entities to DTOs while preserving pagination info</item>
    /// <item>Extracting embedded pagination metadata from a wrapper object</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// Using a result wrapper with pagination metadata:
    /// <code>
    /// public record PagedResult&lt;T&gt;(
    ///     IEnumerable&lt;T&gt; Items,
    ///     long From,
    ///     long To,
    ///     long TotalCount
    /// );
    /// 
    /// [HttpGet]
    /// public ActionResult&lt;IEnumerable&lt;UserDto&gt;&gt; GetUsers(
    ///     [FromQuery] int page = 0,
    ///     [FromQuery] int pageSize = 25)
    /// {
    ///     return _userService
    ///         .GetPagedUsersAsync(page, pageSize)
    ///         .ToActionResult(
    ///             this,
    ///             funcRange: pagedResult => new ContentRangeHeaderValue(
    ///                 pagedResult.From,
    ///                 pagedResult.To,
    ///                 pagedResult.TotalCount),
    ///             funcValue: pagedResult => pagedResult.Items.Select(u => new UserDto(u))
    ///         );
    /// }
    /// 
    /// // Automatically returns 206 Partial Content with proper headers
    /// // for partial results, 200 OK for complete results
    /// </code>
    /// </example>
    public static ActionResult<TOut> ToActionResult<TIn, TOut>(
        this Result<TIn> result,
        ControllerBase controllerBase,
        Func<TIn, ContentRangeHeaderValue> funcRange,
        Func<TIn, TOut> funcValue) =>
        result.Match<TIn, ActionResult<TOut>>(
            onSuccess: inValue =>
            {
                var contentRange = funcRange(inValue);
                var value = funcValue(inValue);

                if (contentRange.From is null || contentRange.To is null || contentRange.Length is null)
                    return controllerBase.Ok(value);

                var partialResult = contentRange.To - contentRange.From + 1 != contentRange.Length;
                if (partialResult)
                    return new PartialContentResult(contentRange, value);

                return controllerBase.Ok(value);
            },
            onFailure: error => error.ToActionResult<TOut>(controllerBase));

    /// <summary>
    /// Converts a <see cref="Result{TValue}"/> to an <see cref="ActionResult{TValue}"/> that returns
    /// 206 Partial Content with a <c>Content-Range</c> header when the result represents a subset,
    /// or 200 OK when it represents the complete set.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the result.</typeparam>
    /// <param name="result">The result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <param name="from">The starting index of the range (zero-indexed, inclusive).</param>
    /// <param name="to">The ending index of the range (zero-indexed, inclusive).</param>
    /// <param name="totalLength">The total number of items available.</param>
    /// <returns>206 Partial Content when the range is a subset, 200 OK when complete, or an error response.</returns>
    public static ActionResult<TValue> ToActionResult<TValue>(
        this Result<TValue> result,
        ControllerBase controllerBase,
        long from,
        long to,
        long totalLength) =>
        result.Match<TValue, ActionResult<TValue>>(
            onSuccess: value =>
            {
                // Guard: invalid, empty, or out-of-range → return 200 OK (no Content-Range)
                if (from < 0 || to < from || totalLength <= 0 || from >= totalLength)
                    return (ActionResult<TValue>)controllerBase.Ok(value);

                // Clamp to against totalLength to prevent ContentRangeHeaderValue from throwing
                var clampedTo = Math.Min(to, totalLength - 1);

                var isCompleteRange = from == 0 && clampedTo == totalLength - 1;
                if (!isCompleteRange)
                    return new PartialContentResult(from, clampedTo, totalLength, value);

                return (ActionResult<TValue>)controllerBase.Ok(value);
            },
            onFailure: error => error.ToActionResult<TValue>(controllerBase));

    /// <summary>
    /// Converts a <see cref="Result{TIn}"/> to an <see cref="ActionResult{TOut}"/> by applying
    /// a mapping function on success, or Problem Details on failure.
    /// </summary>
    /// <typeparam name="TIn">The type of the value contained in the input result.</typeparam>
    /// <typeparam name="TOut">The type of the value in the output ActionResult.</typeparam>
    /// <param name="result">The result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <param name="map">Function that transforms the input value to the output type.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>200 OK with transformed value if result is successful</item>
    /// <item>Appropriate error status code (400-599) based on error type if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// Use this overload when the Mediator handler returns a domain type and the controller
    /// needs to map it to a DTO before returning.
    /// </remarks>
    /// <example>
    /// GET endpoint mapping domain to DTO:
    /// <code>
    /// [HttpGet("{id}")]
    /// public async Task&lt;ActionResult&lt;OrderDto&gt;&gt; GetOrder(OrderId id)
    /// {
    ///     var result = await _sender.Send(new GetOrderByIdQuery(id), ct);
    ///     return result.ToActionResult(this, OrderDto.From);
    /// }
    /// </code>
    /// </example>
    public static ActionResult<TOut> ToActionResult<TIn, TOut>(
        this Result<TIn> result,
        ControllerBase controllerBase,
        Func<TIn, TOut> map) =>
        result.Match<TIn, ActionResult<TOut>>(
            onSuccess: inValue => controllerBase.Ok(map(inValue)),
            onFailure: error => error.ToActionResult<TOut>(controllerBase));

    /// <summary>
    /// Converts a <see cref="Result{TValue}"/> to an <see cref="ActionResult{TValue}"/> that returns
    /// 201 Created with a Location header on success, or Problem Details on failure.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the result.</typeparam>
    /// <param name="result">The result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <param name="actionName">The name of the action to use for generating the Location header URL.</param>
    /// <param name="routeValues">A function that extracts route values from the result value for URL generation.</param>
    /// <param name="controllerName">Optional controller name. When null, the current controller is used.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>201 Created with Location header and value if result is successful</item>
    /// <item>Appropriate error status code (400-599) based on error type if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method is ideal for POST endpoints that create a resource and need to return
    /// a 201 Created response with a Location header pointing to the GET endpoint for the new resource.
    /// </para>
    /// </remarks>
    /// <example>
    /// POST endpoint creating a user:
    /// <code>
    /// [HttpPost]
    /// public ActionResult&lt;UserDto&gt; CreateUser(CreateUserRequest request) =>
    ///     EmailAddress.TryCreate(request.Email)
    ///         .Combine(FirstName.TryCreate(request.FirstName))
    ///         .Bind((email, name) => _userService.CreateUser(email, name))
    ///         .Map(user => new UserDto(user))
    ///         .ToCreatedAtActionResult(this,
    ///             actionName: nameof(GetUser),
    ///             routeValues: dto => new { id = dto.Id });
    ///
    /// // Success: 201 Created with Location: /api/users/{id}
    /// // Validation error: 400 Bad Request with Problem Details
    /// </code>
    /// </example>
    public static ActionResult<TValue> ToCreatedAtActionResult<TValue>(
        this Result<TValue> result,
        ControllerBase controllerBase,
        string actionName,
        Func<TValue, object?> routeValues,
        string? controllerName = null) =>
        result.Match<TValue, ActionResult<TValue>>(
            onSuccess: value => (ActionResult<TValue>)controllerBase.CreatedAtAction(actionName, controllerName, routeValues(value), value),
            onFailure: error => error.ToActionResult<TValue>(controllerBase));

    /// <summary>
    /// Converts a <see cref="Result{TValue}"/> to an <see cref="ActionResult{TOut}"/> that returns
    /// 201 Created with a Location header on success (with value transformation), or Problem Details on failure.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the input result.</typeparam>
    /// <typeparam name="TOut">The type of the value in the output ActionResult.</typeparam>
    /// <param name="result">The result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <param name="actionName">The name of the action to use for generating the Location header URL.</param>
    /// <param name="routeValues">A function that extracts route values from the result value for URL generation.</param>
    /// <param name="map">A function that transforms the input value to the output type for the response body.</param>
    /// <param name="controllerName">Optional controller name. When null, the current controller is used.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>201 Created with Location header and mapped value if result is successful</item>
    /// <item>Appropriate error status code (400-599) based on error type if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// Use this overload when the domain entity needs to be transformed (e.g., to a DTO)
    /// before including it in the response body.
    /// </para>
    /// </remarks>
    /// <example>
    /// POST endpoint creating a user with entity-to-DTO mapping:
    /// <code>
    /// [HttpPost]
    /// public ActionResult&lt;UserDto&gt; CreateUser(CreateUserRequest request) =>
    ///     EmailAddress.TryCreate(request.Email)
    ///         .Combine(FirstName.TryCreate(request.FirstName))
    ///         .Bind((email, name) => _userService.CreateUser(email, name))
    ///         .ToCreatedAtActionResult(this,
    ///             actionName: nameof(GetUser),
    ///             routeValues: user => new { id = user.Id },
    ///             map: user => new UserDto(user));
    ///
    /// // Success: 201 Created with Location: /api/users/{id} and UserDto body
    /// // Validation error: 400 Bad Request with Problem Details
    /// </code>
    /// </example>
    public static ActionResult<TOut> ToCreatedAtActionResult<TValue, TOut>(
        this Result<TValue> result,
        ControllerBase controllerBase,
        string actionName,
        Func<TValue, object?> routeValues,
        Func<TValue, TOut> map,
        string? controllerName = null) =>
        result.Match<TValue, ActionResult<TOut>>(
            onSuccess: inValue => (ActionResult<TOut>)controllerBase.CreatedAtAction(actionName, controllerName, routeValues(inValue), map(inValue)),
            onFailure: error => error.ToActionResult<TOut>(controllerBase));

    /// <summary>
    /// Converts a <see cref="Result{TIn}"/> to an <see cref="ActionResult{TOut}"/> that returns
    /// 201 Created with a Location header and representation metadata headers (ETag, Last-Modified, etc.)
    /// on success, or Problem Details on failure.
    /// </summary>
    /// <typeparam name="TIn">The type of the value contained in the input result.</typeparam>
    /// <typeparam name="TOut">The type of the value in the output ActionResult.</typeparam>
    /// <param name="result">The result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <param name="actionName">The name of the action to use for generating the Location header URL.</param>
    /// <param name="routeValues">A function that extracts route values from the result value for URL generation.</param>
    /// <param name="metadataSelector">Function to extract metadata from the domain value (e.g., build ETag from aggregate).</param>
    /// <param name="map">A function that transforms the input value to the output type for the response body.</param>
    /// <param name="controllerName">Optional controller name. When null, the current controller is used.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>201 Created with Location header, metadata headers, and mapped value if result is successful</item>
    /// <item>Appropriate error status code (400-599) based on error type if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// Use this overload when a POST creates a resource and the client needs an ETag for subsequent
    /// conditional updates (optimistic concurrency).
    /// </remarks>
    /// <example>
    /// POST endpoint creating a resource with ETag:
    /// <code>
    /// [HttpPost]
    /// public ActionResult&lt;OrderDto&gt; CreateOrder(CreateOrderRequest request) =>
    ///     _orderService.CreateOrder(request)
    ///         .ToCreatedAtActionResult(this,
    ///             actionName: nameof(GetOrder),
    ///             routeValues: order => new { id = order.Id },
    ///             metadataSelector: order => RepresentationMetadata.WithStrongETag(order.Version.ToString()),
    ///             map: order => new OrderDto(order));
    /// </code>
    /// </example>
    public static ActionResult<TOut> ToCreatedAtActionResult<TIn, TOut>(
        this Result<TIn> result,
        ControllerBase controllerBase,
        string actionName,
        Func<TIn, object?> routeValues,
        Func<TIn, RepresentationMetadata> metadataSelector,
        Func<TIn, TOut> map,
        string? controllerName = null) =>
        result.Match<TIn, ActionResult<TOut>>(
            onSuccess: inValue =>
            {
                var metadata = metadataSelector(inValue);
                ApplyMetadataHeaders(controllerBase.Response, metadata);
                return (ActionResult<TOut>)controllerBase.CreatedAtAction(actionName, controllerName, routeValues(inValue), map(inValue));
            },
            onFailure: error => error.ToActionResult<TOut>(controllerBase));

    internal static void EmitCompanionHeaders(Error error, Microsoft.AspNetCore.Http.HttpResponse response)
    {
        switch (error)
        {
            case Error.MethodNotAllowed mae:
                response.Headers["Allow"] = string.Join(", ", mae.Allow.Items);
                break;

            case Error.TooManyRequests { RetryAfter: not null } tmr:
                response.Headers["Retry-After"] = tmr.RetryAfter.ToHeaderValue();
                break;

            case Error.ServiceUnavailable { RetryAfter: not null } sue:
                response.Headers["Retry-After"] = sue.RetryAfter.ToHeaderValue();
                break;

            case Error.RangeNotSatisfiable rnse:
                response.Headers["Content-Range"] = $"{rnse.Unit} */{rnse.CompleteLength}";
                break;
        }
    }

    private static ActionResult<TValue> ValidationErrors<TValue>(string? detail, Error.UnprocessableContent validation, string? instance, ControllerBase controllerBase, int statusCode)
    {
        ModelStateDictionary modelState = new();
        foreach (var fieldViolation in validation.Fields)
            modelState.AddModelError(fieldViolation.Field.Path.TrimStart('/'), fieldViolation.Detail ?? fieldViolation.ReasonCode);

        // Pass null when statusCode matches the ValidationProblem default (400)
        // to preserve standard ControllerBase.ValidationProblem behavior.
        int? effectiveStatusCode = statusCode == StatusCodes.Status400BadRequest ? null : statusCode;
        var result = controllerBase.ValidationProblem(detail, instance, effectiveStatusCode, modelStateDictionary: modelState);
        ApplyExtensions(result, validation, validation.Rules);
        return result;
    }

    private static void ApplyExtensions(ActionResult? result, Error error, EquatableArray<RuleViolation> rules)
    {
        if (result is not ObjectResult { Value: ProblemDetails pd })
            return;

        pd.Extensions["code"] = error.Code;
        pd.Extensions["kind"] = error.Kind;

        if (error is Error.InternalServerError ise)
        {
            pd.Extensions["faultId"] = ise.FaultId;
        }

        if (rules.Items.Length > 0)
        {
            pd.Extensions["rules"] = rules.Items
                .Select(rv => (object)new
                {
                    code = rv.ReasonCode,
                    detail = rv.Detail,
                    fields = rv.Fields.Items.Select(p => p.Path).ToArray(),
                })
                .ToArray();
        }
    }

    #region RepresentationMetadata Support

    /// <summary>
    /// Converts a Result to an ActionResult, applying dynamically-computed representation metadata headers
    /// (ETag, Last-Modified, Vary, Content-Language, Content-Location, Accept-Ranges).
    /// Evaluates all conditional request headers (If-Match, If-Unmodified-Since, If-None-Match,
    /// If-Modified-Since) with correct RFC 9110 §13.2.2 precedence via <see cref="ConditionalRequestEvaluator"/>.
    /// </summary>
    /// <typeparam name="TIn">The domain type in the result.</typeparam>
    /// <typeparam name="TOut">The mapped output type for the response body.</typeparam>
    /// <param name="result">The result to convert.</param>
    /// <param name="controller">The controller context.</param>
    /// <param name="metadataSelector">Function to extract metadata from the domain value (e.g., build ETag from aggregate).
    /// For static metadata, use <c>_ =&gt; metadata</c>.</param>
    /// <param name="map">Function to transform the domain value to a response DTO.</param>
    /// <returns>An ActionResult with metadata headers and conditional request evaluation.</returns>
    /// <remarks>
    /// The selector is not invoked when the result is a failure.
    /// </remarks>
    public static ActionResult<TOut> ToActionResult<TIn, TOut>(
        this Result<TIn> result,
        ControllerBase controller,
        Func<TIn, RepresentationMetadata> metadataSelector,
        Func<TIn, TOut> map) =>
        result.Match<TIn, ActionResult<TOut>>(
            onSuccess: inValue =>
            {
                var metadata = metadataSelector(inValue);
                ApplyMetadataHeaders(controller.Response, metadata);

                // Only evaluate conditional headers for safe methods (GET/HEAD).
                // For unsafe methods, the precondition was already checked before the write
                // (via OptionalETag/RequireETag), and the response ETag is the NEW value.
                var method = controller.Request.Method;
                if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
                {
                    return ConditionalRequestEvaluator.Evaluate(controller.Request, metadata) switch
                    {
                        ConditionalDecision.NotModified => new StatusCodeResult(StatusCodes.Status304NotModified),
                        ConditionalDecision.PreconditionFailed =>
                            new Error.PreconditionFailed(new ResourceRef(typeof(TIn).Name, null), PreconditionKind.IfMatch) { Detail = "A conditional request header evaluated to false." }
                                .ToActionResult<TOut>(controller),
                        _ => controller.Ok(map(inValue)),
                    };
                }

                return controller.Ok(map(inValue));
            },
            onFailure: error => error.ToActionResult<TOut>(controller));

    /// <summary>Async Task overload of metadata-selector-aware ToActionResult.</summary>
    public static async Task<ActionResult<TOut>> ToActionResultAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask, ControllerBase controller, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map) =>
        (await resultTask.ConfigureAwait(false)).ToActionResult(controller, metadataSelector, map);

    /// <summary>Async ValueTask overload of metadata-selector-aware ToActionResult.</summary>
    public static async ValueTask<ActionResult<TOut>> ToActionResultAsync<TIn, TOut>(
        this ValueTask<Result<TIn>> resultTask, ControllerBase controller, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map) =>
        (await resultTask.ConfigureAwait(false)).ToActionResult(controller, metadataSelector, map);

    internal static void ApplyMetadataHeaders(HttpResponse response, RepresentationMetadata metadata)
    {
        if (metadata.ETag is not null)
            response.Headers.ETag = metadata.ETag.ToHeaderValue();
        if (metadata.LastModified.HasValue)
            response.Headers["Last-Modified"] = metadata.LastModified.Value.ToString("R");
        if (metadata.Vary is { Count: > 0 })
            response.Headers.Vary = string.Join(", ", metadata.Vary);
        if (metadata.ContentLanguage is { Count: > 0 })
            response.Headers.ContentLanguage = string.Join(", ", metadata.ContentLanguage);
        if (metadata.ContentLocation is not null)
            response.Headers["Content-Location"] = metadata.ContentLocation;
        if (metadata.AcceptRanges is not null)
            response.Headers["Accept-Ranges"] = metadata.AcceptRanges;
    }

    #endregion
}