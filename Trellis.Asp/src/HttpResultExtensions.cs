namespace Trellis.Asp;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Trellis;

/// <summary>
/// Provides extension methods to convert Result types to ASP.NET Core Minimal API <see cref="Microsoft.AspNetCore.Http.IResult"/> responses.
/// These methods bridge Railway Oriented Programming with ASP.NET Core Minimal APIs.
/// </summary>
/// <remarks>
/// <para>
/// These extensions enable clean, functional-style Minimal API endpoints by automatically mapping
/// domain Result types to appropriate HTTP responses. They provide the same functionality as
/// <see cref="ActionResultExtensions"/> but for the Minimal API programming model (ASP.NET Core 6+).
/// </para>
/// <para>
/// Key features:
/// <list type="bullet">
/// <item>Automatic HTTP status code selection based on error type</item>
/// <item>Problem Details (RFC 9457) formatting for errors</item>
/// <item>Validation error formatting with field-level details</item>
/// <item>Unit result to 204 No Content conversion</item>
/// <item>Clean, declarative endpoint definitions</item>
/// </list>
/// </para>
/// <para>
/// Usage pattern in Minimal APIs:
/// <code>
/// app.MapGet("/users/{id}", (string id, IUserService userService) =>
///     UserId.TryCreate(id)
///         .Bind(userService.GetUser)
///         .Map(user => new UserDto(user))
///         .ToHttpResult()
/// );
/// </code>
/// </para>
/// </remarks>
public static class HttpResultExtensions
{
    /// <summary>
    /// Converts a <see cref="Result{TValue}"/> to an <see cref="Microsoft.AspNetCore.Http.IResult"/> with appropriate HTTP status code.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the result.</typeparam>
    /// <param name="result">The result object to convert.</param>
    /// <param name="options">Optional custom error-to-status-code mappings. When null, uses default mappings.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>200 OK with value if result is successful (except for Unit)</item>
    /// <item>204 No Content if result is successful and TValue is Unit</item>
    /// <item>Appropriate error status code (400-599) based on error type if result is failure</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the primary method for converting domain results to HTTP responses in Minimal APIs.
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
    /// app.MapGet("/users/{id}", (Guid id, IUserRepository repository) =>
    ///     UserId.TryCreate(id)
    ///         .Bind(repository.GetAsync)
    ///         .Map(user => new UserDto(user))
    ///         .ToHttpResult());
    /// 
    /// // Success: 200 OK with UserDto
    /// // Not found: 404 Not Found with Problem Details
    /// // Validation error: 400 Bad Request with validation details
    /// </code>
    /// </example>
    /// <example>
    /// POST endpoint returning 201 Created with Location header:
    /// <code>
    /// app.MapPost("/users", (CreateUserRequest request, IUserService userService) =>
    ///     EmailAddress.TryCreate(request.Email)
    ///         .Combine(FirstName.TryCreate(request.FirstName))
    ///         .Bind((email, name) => userService.CreateUser(email, name))
    ///         .ToCreatedAtRouteHttpResult(
    ///             routeName: "GetUser",
    ///             routeValues: user => new RouteValueDictionary(new { id = user.Id }),
    ///             map: user => new UserDto(user)));
    /// 
    /// // Success: 201 Created with Location: /api/users/{id}
    /// // Validation error: 400 Bad Request with field-level errors
    /// // Conflict: 409 Conflict if user already exists
    /// </code>
    /// </example>
    /// <example>
    /// DELETE endpoint returning Unit:
    /// <code>
    /// app.MapDelete("/users/{id}", (Guid id, IUserRepository repository) =>
    ///     UserId.TryCreate(id)
    ///         .Bind(repository.DeleteAsync)
    ///         .ToHttpResult());
    /// 
    /// // Success: 204 No Content (automatic for Unit)
    /// // Not found: 404 Not Found
    /// </code>
    /// </example>
    /// <example>
    /// Complex endpoint with multiple operations:
    /// <code>
    /// app.MapPost("/orders", async (
    ///     CreateOrderRequest request,
    ///     IOrderService orderService,
    ///     IEventBus eventBus) =>
    ///     await CustomerId.TryCreate(request.CustomerId)
    ///         .BindAsync(orderService.GetCustomerAsync)
    ///         .BindAsync(customer => orderService.CreateOrderAsync(customer, request.Items))
    ///         .TapAsync(async order => await eventBus.PublishAsync(new OrderCreatedEvent(order.Id)))
    ///         .MapAsync(order => new OrderDto(order))
    ///         .ToHttpResultAsync());
    /// </code>
    /// </example>
    public static Microsoft.AspNetCore.Http.IResult ToHttpResult<TValue>(this Result<TValue> result, TrellisAspOptions? options = null)
    {
        if (result.IsSuccess)
        {
            // If TValue is Unit, return 204 No Content
            if (typeof(TValue) == typeof(Unit))
                return Results.NoContent();

            return Results.Ok(result.Value);
        }

        return result.Error.ToHttpResult(options);
    }

    /// <summary>
    /// Converts a domain <see cref="Error"/> to an <see cref="Microsoft.AspNetCore.Http.IResult"/> with appropriate HTTP status code and Problem Details format.
    /// </summary>
    /// <param name="error">The domain error to convert.</param>
    /// <param name="options">Optional custom error-to-status-code mappings. When null, uses default mappings.</param>
    /// <returns>
    /// An IResult with Problem Details (RFC 9457) response. The HTTP status code is resolved
    /// from <see cref="TrellisAspOptions"/> (configured via <c>AddTrellisAsp</c>). The default mappings are:
    /// <list type="table">
    ///     <listheader>
    ///         <term>Domain Error Type</term>
    ///         <description>Default HTTP Status Code</description>
    ///     </listheader>
    ///     <item>
    ///         <term><see cref="ValidationError"/></term>
    ///         <description>400 Bad Request with validation problem details and field errors</description>
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
    /// For <see cref="ValidationError"/>, the response includes an additional <c>errors</c> object
    /// containing field-level validation messages grouped by field name.
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
    ///     "email": ["Email address is invalid", "Email is required"],
    ///     "age": ["Age must be 18 or older"]
    ///   }
    /// }
    /// </code>
    /// </example>
    /// <example>
    /// Using custom error types in domain logic:
    /// <code>
    /// public class UserService
    /// {
    ///     public Result&lt;User&gt; GetUser(UserId id)
    ///     {
    ///         var user = _repository.FindById(id);
    ///         if (user == null)
    ///             return Error.NotFound($"User {id} not found", $"/users/{id}");
    ///             
    ///         return user;
    ///     }
    ///     
    ///     public Result&lt;User&gt; CreateUser(EmailAddress email, FirstName name)
    ///     {
    ///         if (_repository.ExistsByEmail(email))
    ///             return Error.Conflict("User with this email already exists");
    ///             
    ///         var user = User.Create(email, name);
    ///         _repository.Add(user);
    ///         return user;
    ///     }
    /// }
    /// 
    /// // Minimal API endpoint
    /// app.MapGet("/users/{id}", (string id, UserService service) =>
    ///     UserId.TryCreate(id)
    ///         .Bind(service.GetUser)
    ///         .ToHttpResult());
    ///         
    /// // Error automatically mapped to 404 Not Found with Problem Details
    /// </code>
    /// </example>
    public static Microsoft.AspNetCore.Http.IResult ToHttpResult(this Error error, TrellisAspOptions? options = null)
    {
        var statusCode = (options ?? TrellisAspOptions.Default).GetStatusCode(error);

        Microsoft.AspNetCore.Http.IResult inner;
        if (error is ValidationError validationError)
        {
            Dictionary<string, string[]> errors = validationError.FieldErrors
                .GroupBy(x => x.FieldName)
                .ToDictionary(x => x.Key, x => x.SelectMany(y => y.Details).ToArray());

            inner = Results.ValidationProblem(errors, validationError.Detail, validationError.Instance, statusCode);
        }
        else
        {
            var detail = statusCode >= 500 ? "An internal error occurred." : error.Detail;
            inner = Results.Problem(detail, error.Instance, statusCode);
        }

        if (HasCompanionHeaders(error))
            return new CompanionHeaderResult(error, inner);

        return inner;
    }

    /// <summary>
    /// Converts a <see cref="Result{TValue}"/> to an <see cref="Microsoft.AspNetCore.Http.IResult"/> that returns
    /// 201 Created with a Location header on success, or Problem Details on failure.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the result.</typeparam>
    /// <param name="result">The result object to convert.</param>
    /// <param name="routeName">The name of the route to use for generating the Location header URL.</param>
    /// <param name="routeValues">A function that extracts route values from the result value for URL generation.
    /// Return a <see cref="RouteValueDictionary"/> to remain AOT-compatible.</param>
    /// <param name="options">Optional custom error-to-status-code mappings. When null, uses default mappings.</param>
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
    /// POST Minimal API endpoint creating a user:
    /// <code>
    /// app.MapPost("/users", (CreateUserRequest request, IUserService service) =>
    ///     EmailAddress.TryCreate(request.Email)
    ///         .Bind(email => service.CreateUser(email))
    ///         .Map(user => new UserDto(user))
    ///         .ToCreatedAtRouteHttpResult(
    ///             routeName: "GetUser",
    ///             routeValues: dto => new RouteValueDictionary(new { id = dto.Id })));
    /// </code>
    /// </example>
    public static Microsoft.AspNetCore.Http.IResult ToCreatedAtRouteHttpResult<TValue>(
        this Result<TValue> result,
        string routeName,
        Func<TValue, RouteValueDictionary> routeValues,
        TrellisAspOptions? options = null)
    {
        if (result.IsSuccess)
            return Results.CreatedAtRoute(routeName, routeValues(result.Value), result.Value);

        return result.Error.ToHttpResult(options);
    }

    /// <summary>
    /// Converts a <see cref="Result{TValue}"/> to an <see cref="Microsoft.AspNetCore.Http.IResult"/> that returns
    /// 201 Created with a Location header on success (with value transformation), or Problem Details on failure.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the input result.</typeparam>
    /// <typeparam name="TOut">The type of the value in the response body.</typeparam>
    /// <param name="result">The result object to convert.</param>
    /// <param name="routeName">The name of the route to use for generating the Location header URL.</param>
    /// <param name="routeValues">A function that extracts route values from the result value for URL generation.
    /// Return a <see cref="RouteValueDictionary"/> to remain AOT-compatible.</param>
    /// <param name="map">A function that transforms the input value to the output type for the response body.</param>
    /// <param name="options">Optional custom error-to-status-code mappings. When null, uses default mappings.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>201 Created with Location header and mapped value if result is successful</item>
    /// <item>Appropriate error status code (400-599) based on error type if result is failure</item>
    /// </list>
    /// </returns>
    /// <example>
    /// POST Minimal API endpoint with entity-to-DTO mapping:
    /// <code>
    /// app.MapPost("/users", (CreateUserRequest request, IUserService service) =>
    ///     EmailAddress.TryCreate(request.Email)
    ///         .Bind(email => service.CreateUser(email))
    ///         .ToCreatedAtRouteHttpResult(
    ///             routeName: "GetUser",
    ///             routeValues: user => new RouteValueDictionary(new { id = user.Id }),
    ///             map: user => new UserDto(user)));
    /// </code>
    /// </example>
    public static Microsoft.AspNetCore.Http.IResult ToCreatedAtRouteHttpResult<TValue, TOut>(
        this Result<TValue> result,
        string routeName,
        Func<TValue, RouteValueDictionary> routeValues,
        Func<TValue, TOut> map,
        TrellisAspOptions? options = null)
    {
        if (result.IsSuccess)
        {
            var value = map(result.Value);
            return Results.CreatedAtRoute(routeName, routeValues(result.Value), value);
        }

        return result.Error.ToHttpResult(options);
    }

    #region RFC 9110 — Metadata-Aware Response (Minimal API)

    /// <summary>
    /// Converts a <see cref="Result{TIn}"/> to a Minimal API <see cref="Microsoft.AspNetCore.Http.IResult"/> with
    /// representation metadata headers (ETag, Last-Modified, Vary, etc.) and conditional request evaluation.
    /// For GET/HEAD, evaluates <c>If-None-Match</c>/<c>If-Modified-Since</c> → 304, and <c>If-Match</c>/<c>If-Unmodified-Since</c> → 412.
    /// </summary>
    public static Microsoft.AspNetCore.Http.IResult ToHttpResult<TIn, TOut>(
        this Result<TIn> result,
        HttpContext httpContext,
        Func<TIn, RepresentationMetadata> metadataSelector,
        Func<TIn, TOut> map,
        TrellisAspOptions? options = null)
    {
        if (result.IsSuccess)
        {
            var metadata = metadataSelector(result.Value);
            ActionResultExtensions.ApplyMetadataHeaders(httpContext.Response, metadata);

            var method = httpContext.Request.Method;
            if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
            {
                return ConditionalRequestEvaluator.Evaluate(httpContext.Request, metadata) switch
                {
                    ConditionalDecision.NotModified => Results.StatusCode(StatusCodes.Status304NotModified),
                    ConditionalDecision.PreconditionFailed =>
                        Error.PreconditionFailed("A conditional request header evaluated to false.")
                            .ToHttpResult(options),
                    _ => Results.Ok(map(result.Value)),
                };
            }

            return Results.Ok(map(result.Value));
        }

        return result.Error.ToHttpResult(options);
    }

    #endregion

    #region RFC 9110 — Created + Metadata (Minimal API)

    /// <summary>
    /// Converts a <see cref="Result{TIn}"/> to a 201 Created Minimal API <see cref="Microsoft.AspNetCore.Http.IResult"/>
    /// with a URI, representation metadata headers, and a mapping function.
    /// </summary>
    public static Microsoft.AspNetCore.Http.IResult ToCreatedHttpResult<TIn, TOut>(
        this Result<TIn> result,
        HttpContext httpContext,
        Func<TIn, string> uriSelector,
        Func<TIn, RepresentationMetadata> metadataSelector,
        Func<TIn, TOut> map,
        TrellisAspOptions? options = null)
    {
        if (result.IsSuccess)
        {
            var metadata = metadataSelector(result.Value);
            ActionResultExtensions.ApplyMetadataHeaders(httpContext.Response, metadata);

            return Results.Created(uriSelector(result.Value), map(result.Value));
        }

        return result.Error.ToHttpResult(options);
    }

    #endregion

    #region RFC 9110 — Partial Content / Pagination (Minimal API)

    /// <summary>
    /// Converts a <see cref="Result{TValue}"/> to a Minimal API <see cref="Microsoft.AspNetCore.Http.IResult"/>
    /// that returns 206 Partial Content with a <c>Content-Range</c> header when the result represents a subset,
    /// or 200 OK when it represents the complete set.
    /// </summary>
    /// <typeparam name="TValue">The type of the value contained in the result.</typeparam>
    /// <param name="result">The result object to convert.</param>
    /// <param name="from">The starting index of the range (zero-indexed, inclusive).</param>
    /// <param name="to">The ending index of the range (zero-indexed, inclusive).</param>
    /// <param name="totalLength">The total number of items available.</param>
    /// <param name="options">Optional custom error-to-status-code mappings.</param>
    /// <returns>206 Partial Content when the range is a subset, 200 OK when complete, or an error response.</returns>
    public static Microsoft.AspNetCore.Http.IResult ToHttpResult<TValue>(
        this Result<TValue> result,
        long from,
        long to,
        long totalLength,
        TrellisAspOptions? options = null)
    {
        if (result.IsSuccess)
        {
            // Guard: invalid or empty range → return 200 OK (no Content-Range)
            if (to < from || totalLength <= 0)
                return Results.Ok(result.Value);

            var partialResult = to - from + 1 != totalLength;
            if (partialResult)
                return new PartialContentHttpResult(from, to, totalLength, Results.Ok(result.Value));

            return Results.Ok(result.Value);
        }

        return result.Error.ToHttpResult(options);
    }

    /// <summary>
    /// Converts a <see cref="Result{TIn}"/> to a Minimal API <see cref="Microsoft.AspNetCore.Http.IResult"/>
    /// that returns 206 Partial Content when the lambda-provided <see cref="System.Net.Http.Headers.ContentRangeHeaderValue"/>
    /// indicates a partial range, or 200 OK when the response contains the complete set.
    /// </summary>
    /// <typeparam name="TIn">The type of the value contained in the input result.</typeparam>
    /// <typeparam name="TOut">The type of the value in the response body.</typeparam>
    /// <param name="result">The result object to convert.</param>
    /// <param name="funcRange">A function that extracts the Content-Range information from the result value.</param>
    /// <param name="funcValue">A function that maps the result value to the response body.</param>
    /// <param name="options">Optional custom error-to-status-code mappings.</param>
    /// <returns>206 Partial Content when partial, 200 OK when complete, or an error response.</returns>
    public static Microsoft.AspNetCore.Http.IResult ToHttpResult<TIn, TOut>(
        this Result<TIn> result,
        Func<TIn, System.Net.Http.Headers.ContentRangeHeaderValue> funcRange,
        Func<TIn, TOut> funcValue,
        TrellisAspOptions? options = null)
    {
        if (result.IsSuccess)
        {
            var contentRange = funcRange(result.Value);
            var value = funcValue(result.Value);

            if (contentRange.From is null || contentRange.To is null || contentRange.Length is null)
                return Results.Ok(value);

            var isCompleteRange = contentRange.From == 0 && contentRange.To == contentRange.Length - 1;
            if (!isCompleteRange)
                return new PartialContentHttpResult(contentRange, Results.Ok(value));

            return Results.Ok(value);
        }

        return result.Error.ToHttpResult(options);
    }

    #endregion

    #region RFC 7240 — ToUpdatedHttpResult / Prefer Support (Minimal API)

    /// <summary>
    /// Converts a successful <see cref="Result{TIn}"/> to an updated response for Minimal APIs,
    /// honoring RFC 7240 <c>Prefer</c>. Returns 200 OK with body by default, or 204 No Content
    /// when <c>Prefer: return=minimal</c> is present. On failure, returns the appropriate error response.
    /// </summary>
    /// <typeparam name="TIn">The domain type in the result.</typeparam>
    /// <typeparam name="TOut">The mapped output type for the response body.</typeparam>
    /// <param name="result">The result from the update operation.</param>
    /// <param name="httpContext">The HTTP context (used to read Prefer header and set response headers).</param>
    /// <param name="metadata">Optional representation metadata (ETag, Last-Modified, etc.).</param>
    /// <param name="map">Function to transform the domain value to a response DTO.</param>
    /// <param name="options">Optional custom error-to-status-code mappings.</param>
    /// <returns>An IResult with appropriate status code and headers.</returns>
    public static Microsoft.AspNetCore.Http.IResult ToUpdatedHttpResult<TIn, TOut>(
        this Result<TIn> result,
        HttpContext httpContext,
        RepresentationMetadata? metadata,
        Func<TIn, TOut> map,
        TrellisAspOptions? options = null)
    {
        if (result.IsFailure)
            return result.Error.ToHttpResult(options);

        var outcome = new WriteOutcome<TIn>.Updated(result.Value, metadata);
        return outcome.ToHttpResult<TIn, TOut>(httpContext, map);
    }

    /// <summary>
    /// Converts a successful <see cref="Result{TIn}"/> to an updated response for Minimal APIs with dynamic metadata,
    /// honoring RFC 7240 <c>Prefer</c>.
    /// </summary>
    /// <typeparam name="TIn">The domain type in the result.</typeparam>
    /// <typeparam name="TOut">The mapped output type for the response body.</typeparam>
    /// <param name="result">The result from the update operation.</param>
    /// <param name="httpContext">The HTTP context (used to read Prefer header and set response headers).</param>
    /// <param name="metadataSelector">Function to build metadata from the domain value (e.g., extract ETag).</param>
    /// <param name="map">Function to transform the domain value to a response DTO.</param>
    /// <param name="options">Optional custom error-to-status-code mappings.</param>
    /// <returns>An IResult with appropriate status code and headers.</returns>
    public static Microsoft.AspNetCore.Http.IResult ToUpdatedHttpResult<TIn, TOut>(
        this Result<TIn> result,
        HttpContext httpContext,
        Func<TIn, RepresentationMetadata> metadataSelector,
        Func<TIn, TOut> map,
        TrellisAspOptions? options = null)
    {
        if (result.IsFailure)
            return result.Error.ToHttpResult(options);

        var metadata = metadataSelector(result.Value);
        var outcome = new WriteOutcome<TIn>.Updated(result.Value, metadata);
        return outcome.ToHttpResult<TIn, TOut>(httpContext, map);
    }

    #endregion

    private static bool HasCompanionHeaders(Error error) => error switch
    {
        MethodNotAllowedError => true,
        RateLimitError { RetryAfter: not null } => true,
        ServiceUnavailableError { RetryAfter: not null } => true,
        ContentTooLargeError { RetryAfter: not null } => true,
        RangeNotSatisfiableError => true,
        _ => false,
    };

    /// <summary>
    /// An <see cref="Microsoft.AspNetCore.Http.IResult"/> wrapper that writes RFC-required companion headers
    /// before delegating to the inner result.
    /// </summary>
    private sealed class CompanionHeaderResult(Error error, Microsoft.AspNetCore.Http.IResult inner) : Microsoft.AspNetCore.Http.IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ActionResultExtensions.EmitCompanionHeaders(error, httpContext.Response);
            await inner.ExecuteAsync(httpContext).ConfigureAwait(false);
        }
    }
}