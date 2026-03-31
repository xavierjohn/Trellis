namespace Trellis.Asp.Tests;

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Xunit;

/// <summary>
/// Tests for <see cref="TrellisAspOptions"/> default mappings, override behavior,
/// and <c>AddTrellisAsp</c> integration.
/// </summary>
[Collection("TrellisAspOptionsState")]
public class TrellisAspOptionsTests : IDisposable
{
    public TrellisAspOptionsTests() => TrellisAspOptions.ResetCurrent();

    public void Dispose()
    {
        TrellisAspOptions.ResetCurrent();
        GC.SuppressFinalize(this);
    }

    #region Default Mappings

    [Fact]
    public void GetStatusCode_ValidationError_returns_400()
    {
        var options = new TrellisAspOptions();
        var error = Error.Validation("Invalid", "field");

        options.GetStatusCode(error).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void GetStatusCode_BadRequestError_returns_400()
    {
        var options = new TrellisAspOptions();
        var error = Error.BadRequest("Bad");

        options.GetStatusCode(error).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void GetStatusCode_UnauthorizedError_returns_401()
    {
        var options = new TrellisAspOptions();
        var error = Error.Unauthorized("Nope");

        options.GetStatusCode(error).Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public void GetStatusCode_ForbiddenError_returns_403()
    {
        var options = new TrellisAspOptions();
        var error = Error.Forbidden("Denied");

        options.GetStatusCode(error).Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void GetStatusCode_NotFoundError_returns_404()
    {
        var options = new TrellisAspOptions();
        var error = Error.NotFound("Missing");

        options.GetStatusCode(error).Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void GetStatusCode_ConflictError_returns_409()
    {
        var options = new TrellisAspOptions();
        var error = Error.Conflict("Conflict");

        options.GetStatusCode(error).Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void GetStatusCode_PreconditionFailedError_returns_412()
    {
        var options = new TrellisAspOptions();
        var error = Error.PreconditionFailed("Stale ETag");

        options.GetStatusCode(error).Should().Be(StatusCodes.Status412PreconditionFailed);
    }

    [Fact]
    public void GetStatusCode_PreconditionRequiredError_returns_428()
    {
        var options = new TrellisAspOptions();
        var error = Error.PreconditionRequired("If-Match required");

        options.GetStatusCode(error).Should().Be(StatusCodes.Status428PreconditionRequired);
    }

    [Fact]
    public void GetStatusCode_DomainError_returns_422()
    {
        var options = new TrellisAspOptions();
        var error = Error.Domain("Business rule");

        options.GetStatusCode(error).Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public void GetStatusCode_RateLimitError_returns_429()
    {
        var options = new TrellisAspOptions();
        var error = Error.RateLimit("Too many");

        options.GetStatusCode(error).Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public void GetStatusCode_UnexpectedError_returns_500()
    {
        var options = new TrellisAspOptions();
        var error = Error.Unexpected("Oops");

        options.GetStatusCode(error).Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void GetStatusCode_ServiceUnavailableError_returns_503()
    {
        var options = new TrellisAspOptions();
        var error = Error.ServiceUnavailable("Down");

        options.GetStatusCode(error).Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public void GetStatusCode_UnknownErrorType_returns_500()
    {
        var options = new TrellisAspOptions();
        var error = new Error("Unknown problem", "unknown.error");

        options.GetStatusCode(error).Should().Be(StatusCodes.Status500InternalServerError);
    }

    #endregion

    #region Override Behavior

    [Fact]
    public void MapError_overrides_default_mapping()
    {
        var options = new TrellisAspOptions();
        options.MapError<DomainError>(StatusCodes.Status400BadRequest);

        options.GetStatusCode(Error.Domain("Business rule")).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void MapError_does_not_affect_other_mappings()
    {
        var options = new TrellisAspOptions();
        options.MapError<DomainError>(StatusCodes.Status400BadRequest);

        options.GetStatusCode(Error.NotFound("Missing")).Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void MapError_returns_options_for_fluent_chaining()
    {
        var options = new TrellisAspOptions();

        var result = options.MapError<DomainError>(StatusCodes.Status400BadRequest);

        result.Should().BeSameAs(options);
    }

    [Fact]
    public void MapError_multiple_overrides_applied()
    {
        var options = new TrellisAspOptions();
        options
            .MapError<DomainError>(StatusCodes.Status400BadRequest)
            .MapError<ConflictError>(StatusCodes.Status422UnprocessableEntity);

        options.GetStatusCode(Error.Domain("test")).Should().Be(StatusCodes.Status400BadRequest);
        options.GetStatusCode(Error.Conflict("test")).Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public void MapError_base_Error_type_acts_as_catchall()
    {
        var options = new TrellisAspOptions();
        options.MapError<Error>(StatusCodes.Status418ImATeapot);

        // Custom error with no specific mapping walks up to Error
        var customError = new Error("custom", "custom.error");
        options.GetStatusCode(customError).Should().Be(StatusCodes.Status418ImATeapot);
    }

    [Fact]
    public void MapError_base_Error_type_does_not_override_specific_mappings()
    {
        var options = new TrellisAspOptions();
        options.MapError<Error>(StatusCodes.Status418ImATeapot);

        // Specific error types still use their own mapping
        options.GetStatusCode(Error.NotFound("Missing")).Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void MapError_ValidationError_can_be_overridden()
    {
        var options = new TrellisAspOptions();
        options.MapError<ValidationError>(StatusCodes.Status422UnprocessableEntity);

        options.GetStatusCode(Error.Validation("Bad data", "field")).Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    #endregion

    #region AddTrellisAsp Registration

    [Fact]
    public void AddTrellisAsp_no_args_registers_TrellisAspOptions_in_DI()
    {
        var services = new ServiceCollection();
        services.AddTrellisAsp();

        services.Should().ContainSingle(d => d.ServiceType == typeof(TrellisAspOptions));
    }

    [Fact]
    public void AddTrellisAsp_with_configure_registers_TrellisAspOptions_in_DI()
    {
        var services = new ServiceCollection();
        services.AddTrellisAsp(options =>
            options.MapError<DomainError>(StatusCodes.Status400BadRequest));

        services.Should().ContainSingle(d => d.ServiceType == typeof(TrellisAspOptions));
    }

    [Fact]
    public void AddTrellisAsp_resolved_options_reflect_configured_overrides()
    {
        var services = new ServiceCollection();
        services.AddTrellisAsp(options =>
            options.MapError<DomainError>(StatusCodes.Status400BadRequest));

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<TrellisAspOptions>();

        resolved.GetStatusCode(Error.Domain("test")).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void AddTrellisAsp_no_args_resolved_options_use_defaults()
    {
        var services = new ServiceCollection();
        services.AddTrellisAsp();

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<TrellisAspOptions>();

        resolved.GetStatusCode(Error.Domain("test")).Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    #endregion

    #region New RFC 9110 Error Type Mappings

    [Fact]
    public void GetStatusCode_GoneError_returns_410()
    {
        var options = new TrellisAspOptions();
        options.GetStatusCode(Error.Gone("Permanently removed")).Should().Be(StatusCodes.Status410Gone);
    }

    [Fact]
    public void GetStatusCode_MethodNotAllowedError_returns_405()
    {
        var options = new TrellisAspOptions();
        options.GetStatusCode(Error.MethodNotAllowed("Not allowed", ["GET"])).Should().Be(StatusCodes.Status405MethodNotAllowed);
    }

    [Fact]
    public void GetStatusCode_NotAcceptableError_returns_406()
    {
        var options = new TrellisAspOptions();
        options.GetStatusCode(Error.NotAcceptable("Not acceptable")).Should().Be(StatusCodes.Status406NotAcceptable);
    }

    [Fact]
    public void GetStatusCode_UnsupportedMediaTypeError_returns_415()
    {
        var options = new TrellisAspOptions();
        options.GetStatusCode(Error.UnsupportedMediaType("Unsupported")).Should().Be(StatusCodes.Status415UnsupportedMediaType);
    }

    [Fact]
    public void GetStatusCode_ContentTooLargeError_returns_413()
    {
        var options = new TrellisAspOptions();
        options.GetStatusCode(Error.ContentTooLarge("Too large")).Should().Be(StatusCodes.Status413RequestEntityTooLarge);
    }

    [Fact]
    public void GetStatusCode_RangeNotSatisfiableError_returns_416()
    {
        var options = new TrellisAspOptions();
        options.GetStatusCode(Error.RangeNotSatisfiable("Not satisfiable", 1024)).Should().Be(StatusCodes.Status416RangeNotSatisfiable);
    }

    #endregion
}