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
/// <item>Problem Details (RFC 7807) formatting for errors</item>
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
    /// DELETE endpoint returning Unit:
    /// <code>
    /// [HttpDelete("{id}")]
    /// public ActionResult&lt;Unit&gt; DeleteUser(Guid id) =>
    ///     UserId.TryCreate(id)
    ///         .Bind(_repository.DeleteAsync)
    ///         .ToActionResult(this);
    /// 
    /// // Success: 204 No Content (automatic for Unit)
    /// // Not found: 404 Not Found
    /// </code>
    /// </example>
    public static ActionResult<TValue> ToActionResult<TValue>(this Result<TValue> result, ControllerBase controllerBase)
    {
        if (result.IsSuccess)
        {
            // If TValue is Unit, return 204 No Content
            if (typeof(TValue) == typeof(Unit))
                return (ActionResult<TValue>)controllerBase.NoContent();

            return (ActionResult<TValue>)controllerBase.Ok(result.Value);
        }

        return result.Error.ToActionResult<TValue>(controllerBase);
    }

    /// <summary>
    /// Converts a domain <see cref="Error"/> to an <see cref="ActionResult{TValue}"/> with appropriate HTTP status code and Problem Details format.
    /// </summary>
    /// <typeparam name="TValue">The type of the ActionResult value.</typeparam>
    /// <param name="error">The domain error to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <returns>
    /// An ActionResult with Problem Details (RFC 7807) response. The HTTP status code is resolved
    /// from <see cref="TrellisAspOptions"/> (configured via <c>AddTrellisAsp</c>). The default mappings are:
    /// <list type="table">
    ///     <listheader>
    ///         <term>Domain Error Type</term>
    ///         <description>Default HTTP Status Code</description>
    ///     </listheader>
    ///     <item>
    ///         <term><see cref="ValidationError"/></term>
    ///         <description>400 Bad Request with validation problem details and ModelState</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="BadRequestError"/></term>
    ///         <description>400 Bad Request</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="UnauthorizedError"/></term>
    ///         <description>401 Unauthorized</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="ForbiddenError"/></term>
    ///         <description>403 Forbidden</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="NotFoundError"/></term>
    ///         <description>404 Not Found</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="ConflictError"/></term>
    ///         <description>409 Conflict</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="DomainError"/></term>
    ///         <description>422 Unprocessable Entity</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="RateLimitError"/></term>
    ///         <description>429 Too Many Requests</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="UnexpectedError"/></term>
    ///         <description>500 Internal Server Error</description>
    ///     </item>
    ///     <item>
    ///         <term><see cref="ServiceUnavailableError"/></term>
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
    ///     options.MapError&lt;DomainError&gt;(StatusCodes.Status400BadRequest);
    /// });
    /// </code>
    /// If <c>AddTrellisAsp</c> is not called, the default mappings shown above are used.
    /// </para>
    /// <para>
    /// All responses use Problem Details format (RFC 7807) which provides a standard way to
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
    /// For <see cref="ValidationError"/>, the response includes an additional <c>errors</c> object
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

        if (error is ValidationError validation)
            return ValidationErrors<TValue>(string.IsNullOrEmpty(error.Detail) ? null : error.Detail, validation, error.Instance, controllerBase, statusCode);

        var detail = statusCode >= 500 ? "An internal error occurred." : error.Detail;
        return (ActionResult<TValue>)controllerBase.Problem(detail, error.Instance, statusCode);
    }

    /// <summary>
    /// Converts a <see cref="Result{TValue}"/> to an <see cref="ActionResult{TValue}"/> with support for partial content responses (HTTP 206).
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the result.</typeparam>
    /// <param name="result">The result object to convert.</param>
    /// <param name="controllerBase">The controller context used to create the ActionResult.</param>
    /// <param name="from">The starting index of the range (inclusive, 0-based).</param>
    /// <param name="to">The ending index of the range (inclusive, 0-based).</param>
    /// <param name="length">The total number of items available.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>206 Partial Content with Content-Range header if the range is a subset of the total</item>
    /// <item>200 OK if the range represents the complete set</item>
    /// <item>Appropriate error status code if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method is useful for implementing pagination or range requests where clients
    /// request a subset of a larger collection. The Content-Range header indicates:
    /// <list type="bullet">
    /// <item>Which items are included in this response</item>
    /// <item>The total number of items available</item>
    /// </list>
    /// </para>
    /// <para>
    /// The Content-Range header format is: <c>items {from}-{to}/{length}</c>
    /// Example: <c>Content-Range: items 0-24/100</c>
    /// </para>
    /// </remarks>
    /// <example>
    /// Paginated list endpoint:
    /// <code>
    /// [HttpGet]
    /// public ActionResult&lt;IEnumerable&lt;UserDto&gt;&gt; GetUsers(
    ///     [FromQuery] int page = 0,
    ///     [FromQuery] int pageSize = 25)
    /// {
    ///     var from = page * pageSize;
    ///     var to = from + pageSize - 1;
    ///     
    ///     return _userService
    ///         .GetUsersAsync(from, pageSize)
    ///         .Map(result => (
    ///             Users: result.Items,
    ///             TotalCount: result.TotalCount
    ///         ))
    ///         .Map(x => x.Users)
    ///         .ToActionResult(this, from, to, totalCount);
    /// }
    /// 
    /// // Response for page 0 (25 items out of 100):
    /// // Status: 206 Partial Content
    /// // Content-Range: items 0-24/100
    /// 
    /// // Response for single page (all items):
    /// // Status: 200 OK
    /// // No Content-Range header
    /// </code>
    /// </example>
    public static ActionResult<TValue> ToActionResult<TValue>(this Result<TValue> result, ControllerBase controllerBase, long from, long to, long length)
    {
        if (result.IsSuccess)
        {
            var partialResult = to - from + 1 != length;
            if (partialResult)
                return new PartialObjectResult(from, to, length, result.Value);

            return controllerBase.Ok(result.Value);
        }

        var error = result.Error;
        return error.ToActionResult<TValue>(controllerBase);
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
        Func<TIn, TOut> funcValue)
    {
        if (result.IsSuccess)
        {
            var contentRange = funcRange(result.Value);
            var value = funcValue(result.Value);

            if (contentRange.From is null || contentRange.To is null || contentRange.Length is null)
                return controllerBase.Ok(value);

            var partialResult = contentRange.To - contentRange.From + 1 != contentRange.Length;
            if (partialResult)
                return new PartialObjectResult(contentRange, value);

            return controllerBase.Ok(value);
        }

        var error = result.Error;
        return error.ToActionResult<TOut>(controllerBase);
    }

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
        Func<TIn, TOut> map)
    {
        if (result.IsSuccess)
            return controllerBase.Ok(map(result.Value));

        return result.Error.ToActionResult<TOut>(controllerBase);
    }

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
        string? controllerName = null)
    {
        if (result.IsSuccess)
            return (ActionResult<TValue>)controllerBase.CreatedAtAction(actionName, controllerName, routeValues(result.Value), result.Value);

        return result.Error.ToActionResult<TValue>(controllerBase);
    }

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
        string? controllerName = null)
    {
        if (result.IsSuccess)
        {
            var value = map(result.Value);
            return (ActionResult<TOut>)controllerBase.CreatedAtAction(actionName, controllerName, routeValues(result.Value), value);
        }

        return result.Error.ToActionResult<TOut>(controllerBase);
    }

    private static ActionResult<TValue> ValidationErrors<TValue>(string? detail, ValidationError validation, string? instance, ControllerBase controllerBase, int statusCode)
    {
        ModelStateDictionary modelState = new();
        foreach (var error in validation.FieldErrors)
            foreach (var detailError in error.Details)
                modelState.AddModelError(error.FieldName, detailError);

        // Pass null when statusCode matches the ValidationProblem default (400)
        // to preserve standard ControllerBase.ValidationProblem behavior.
        int? effectiveStatusCode = statusCode == StatusCodes.Status400BadRequest ? null : statusCode;
        return controllerBase.ValidationProblem(detail, instance, effectiveStatusCode, modelStateDictionary: modelState);
    }
}