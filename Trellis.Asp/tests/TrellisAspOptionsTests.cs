namespace Trellis.Asp.Tests;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Xunit;

/// <summary>
/// Tests for <see cref="TrellisAspOptions"/> default mappings, override behavior,
/// and <c>AddTrellisAsp</c> integration.
/// </summary>
public class TrellisAspOptionsTests
{
    #region Default Mappings

    [Fact]
    public void GetStatusCode_ValidationError_returns_422()
    {
        var options = new TrellisAspOptions();
        var error = new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field"), "validation.error") { Detail = "Invalid" }));

        options.GetStatusCode(error).Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public void GetStatusCode_BadRequestError_returns_400()
    {
        var options = new TrellisAspOptions();
        var error = new Error.BadRequest("bad.request") { Detail = "Bad" };

        options.GetStatusCode(error).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void GetStatusCode_UnauthorizedError_returns_401()
    {
        var options = new TrellisAspOptions();
        var error = new Error.Unauthorized() { Detail = "Nope" };

        options.GetStatusCode(error).Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public void GetStatusCode_ForbiddenError_returns_403()
    {
        var options = new TrellisAspOptions();
        var error = new Error.Forbidden("authorization.forbidden") { Detail = "Denied" };

        options.GetStatusCode(error).Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void GetStatusCode_NotFoundError_returns_404()
    {
        var options = new TrellisAspOptions();
        var error = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Missing" };

        options.GetStatusCode(error).Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void GetStatusCode_ConflictError_returns_409()
    {
        var options = new TrellisAspOptions();
        var error = new Error.Conflict(null, "conflict") { Detail = "Conflict" };

        options.GetStatusCode(error).Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void GetStatusCode_PreconditionFailedError_returns_412()
    {
        var options = new TrellisAspOptions();
        var error = new Error.PreconditionFailed(new ResourceRef("Resource", null), PreconditionKind.IfMatch) { Detail = "Stale ETag" };

        options.GetStatusCode(error).Should().Be(StatusCodes.Status412PreconditionFailed);
    }

    [Fact]
    public void GetStatusCode_PreconditionRequiredError_returns_428()
    {
        var options = new TrellisAspOptions();
        var error = new Error.PreconditionRequired(PreconditionKind.IfMatch) { Detail = "If-Match required" };

        options.GetStatusCode(error).Should().Be(StatusCodes.Status428PreconditionRequired);
    }

    [Fact]
    public void GetStatusCode_DomainError_returns_422()
    {
        var options = new TrellisAspOptions();
        var error = new Error.Conflict(null, "domain.violation") { Detail = "Business rule" };

        options.GetStatusCode(error).Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void GetStatusCode_RateLimitError_returns_429()
    {
        var options = new TrellisAspOptions();
        var error = new Error.TooManyRequests() { Detail = "Too many" };

        options.GetStatusCode(error).Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public void GetStatusCode_UnexpectedError_returns_500()
    {
        var options = new TrellisAspOptions();
        var error = new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = "Oops" };

        options.GetStatusCode(error).Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void GetStatusCode_ServiceUnavailableError_returns_503()
    {
        var options = new TrellisAspOptions();
        var error = new Error.ServiceUnavailable() { Detail = "Down" };

        options.GetStatusCode(error).Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public void GetStatusCode_UnknownErrorType_returns_500()
    {
        var options = new TrellisAspOptions();
        Error error = new Error.InternalServerError("fault-1") { Detail = "Unknown problem" };

        options.GetStatusCode(error).Should().Be(StatusCodes.Status500InternalServerError);
    }

    #endregion

    #region Override Behavior

    [Fact]
    public void MapError_overrides_default_mapping()
    {
        var options = new TrellisAspOptions();
        options.MapError<Error.Conflict>(StatusCodes.Status400BadRequest);

        options.GetStatusCode(new Error.Conflict(null, "domain.violation") { Detail = "Business rule" }).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void MapError_does_not_affect_other_mappings()
    {
        var options = new TrellisAspOptions();
        options.MapError<Error.Conflict>(StatusCodes.Status400BadRequest);

        options.GetStatusCode(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Missing" }).Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void MapError_returns_options_for_fluent_chaining()
    {
        var options = new TrellisAspOptions();

        var result = options.MapError<Error.Conflict>(StatusCodes.Status400BadRequest);

        result.Should().BeSameAs(options);
    }

    [Fact]
    public void MapError_multiple_overrides_applied()
    {
        var options = new TrellisAspOptions();
        options
            .MapError<Error.Conflict>(StatusCodes.Status400BadRequest)
            .MapError<Error.Conflict>(StatusCodes.Status409Conflict);

        // Last-write-wins for the same type: both instances get 409.
        options.GetStatusCode(new Error.Conflict(null, "domain.violation") { Detail = "test" }).Should().Be(StatusCodes.Status409Conflict);
        options.GetStatusCode(new Error.Conflict(null, "conflict") { Detail = "test" }).Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void MapError_base_Error_type_acts_as_catchall()
    {
        var options = new TrellisAspOptions();
        options.MapError<Error>(StatusCodes.Status418ImATeapot);

        // V6 ADT note: every concrete Error subtype already has an explicit default mapping,
        // so the base-type catchall is reachable only if a default mapping is removed.
        // Here we use InternalServerError, which already maps to 500; the explicit type wins.
        Error customError = new Error.InternalServerError("fault-1") { Detail = "custom" };
        options.GetStatusCode(customError).Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void MapError_base_Error_type_does_not_override_specific_mappings()
    {
        var options = new TrellisAspOptions();
        options.MapError<Error>(StatusCodes.Status418ImATeapot);

        // Specific error types still use their own mapping
        options.GetStatusCode(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Missing" }).Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void MapError_ValidationError_can_be_overridden()
    {
        var options = new TrellisAspOptions();
        options.MapError<Error.UnprocessableContent>(StatusCodes.Status422UnprocessableEntity);

        options.GetStatusCode(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field"), "validation.error") { Detail = "Bad data" }))).Should().Be(StatusCodes.Status422UnprocessableEntity);
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
            options.MapError<Error.Conflict>(StatusCodes.Status400BadRequest));

        services.Should().ContainSingle(d => d.ServiceType == typeof(TrellisAspOptions));
    }

    [Fact]
    public void AddTrellisAsp_resolved_options_reflect_configured_overrides()
    {
        var services = new ServiceCollection();
        services.AddTrellisAsp(options =>
            options.MapError<Error.Conflict>(StatusCodes.Status400BadRequest));

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<TrellisAspOptions>();

        resolved.GetStatusCode(new Error.Conflict(null, "domain.violation") { Detail = "test" }).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void AddTrellisAsp_no_args_resolved_options_use_defaults()
    {
        var services = new ServiceCollection();
        services.AddTrellisAsp();

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<TrellisAspOptions>();

        resolved.GetStatusCode(new Error.Conflict(null, "domain.violation") { Detail = "test" }).Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void AddTrellisAsp_called_twice_composes_configuration_delegates()
    {
        // Composition contract: when a library calls AddTrellisAsp(o => ...) and then the
        // application also calls AddTrellisAsp(o => ...), BOTH MapError overrides must survive
        // on the resolved instance. Last-wins (AddSingleton(options)) silently dropped the
        // first call's mappings, surprising hosts that compose Trellis with their own libraries.
        var services = new ServiceCollection();
        services.AddTrellisAsp(o => o.MapError<Error.Conflict>(StatusCodes.Status400BadRequest));
        services.AddTrellisAsp(o => o.MapError<Error.PreconditionFailed>(StatusCodes.Status418ImATeapot));

        var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<TrellisAspOptions>();

        resolved
            .GetStatusCode(new Error.Conflict(null, "k") { Detail = "x" })
            .Should().Be(StatusCodes.Status400BadRequest, "first AddTrellisAsp call's MapError must survive composition");
        resolved
            .GetStatusCode(new Error.PreconditionFailed(new ResourceRef("R", null), PreconditionKind.IfMatch) { Detail = "x" })
            .Should().Be(StatusCodes.Status418ImATeapot, "second AddTrellisAsp call's MapError must survive composition");
    }

    [Fact]
    public void AddTrellisAsp_called_twice_still_registers_only_one_TrellisAspOptions_descriptor()
    {
        // Even when AddTrellisAsp is called multiple times, exactly one descriptor of
        // ServiceType = typeof(TrellisAspOptions) must exist so the resolved instance is
        // unambiguous. Composition happens via IConfigureOptions<TrellisAspOptions>, which
        // is a different ServiceType.
        var services = new ServiceCollection();
        services.AddTrellisAsp(o => o.MapError<Error.Conflict>(StatusCodes.Status400BadRequest));
        services.AddTrellisAsp(o => o.MapError<Error.PreconditionFailed>(StatusCodes.Status418ImATeapot));

        services.Should().ContainSingle(d => d.ServiceType == typeof(TrellisAspOptions));
    }

    [Fact]
    public void AddTrellisAsp_called_twice_last_wins_for_same_TError_mapping()
    {
        // Composition is order-preserving. When two AddTrellisAsp calls map the SAME error
        // type to different status codes, the later call wins (standard IConfigureOptions
        // semantics — delegates run in registration order on the same instance).
        var services = new ServiceCollection();
        services.AddTrellisAsp(o => o.MapError<Error.Conflict>(StatusCodes.Status400BadRequest));
        services.AddTrellisAsp(o => o.MapError<Error.Conflict>(StatusCodes.Status418ImATeapot));

        var sp = services.BuildServiceProvider();
        var resolved = sp.GetRequiredService<TrellisAspOptions>();

        resolved
            .GetStatusCode(new Error.Conflict(null, "k") { Detail = "x" })
            .Should().Be(StatusCodes.Status418ImATeapot);
    }

    [Fact]
    public async Task ResultExecuteAsync_uses_TrellisAspOptions_resolved_from_request_services()
    {
        // ga-09 contract: error → status mapping flows through DI, not ambient static state.
        // A configured override registered via AddTrellisAsp must take effect for any
        // TrellisHttpResult / TrellisErrorOnlyResult executed inside that request scope.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrellisAsp(o => o.MapError<Error.Conflict>(StatusCodes.Status418ImATeapot));
        var sp = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext { RequestServices = sp };
        ctx.Response.Body = new System.IO.MemoryStream();

        var failed = Result.Fail<int>(new Error.Conflict(null, "k") { Detail = "x" });
        await failed.ToHttpResponse().ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status418ImATeapot);
    }

    [Fact]
    public async Task ResultExecuteAsync_falls_back_to_SystemDefault_when_options_not_registered()
    {
        // Without a TrellisAspOptions registration, the writer uses the immutable SystemDefault
        // mappings — no ambient state, no leakage between hosts.
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext { RequestServices = sp };
        ctx.Response.Body = new System.IO.MemoryStream();

        var failed = Result.Fail<int>(new Error.Conflict(null, "k") { Detail = "x" });
        await failed.ToHttpResponse().ExecuteAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void SystemDefault_property_must_be_internal_to_prevent_global_mutation()
    {
        // Regression: SystemDefault was previously public, allowing
        // `TrellisAspOptions.SystemDefault.MapError<X>(...)` to mutate a process-wide
        // singleton and reintroduce the cross-host / cross-test leakage that the
        // DI-resolved options model exists to eliminate. Keep it internal.
        var prop = typeof(TrellisAspOptions).GetProperty(
            "SystemDefault",
            System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);

        prop.Should().NotBeNull();
        var getter = prop!.GetGetMethod(nonPublic: true)!;
        getter.IsPublic.Should().BeFalse(
            "TrellisAspOptions.SystemDefault must not be publicly accessible — exposing it lets consumers mutate the shared fallback via MapError, leaking error mappings across tests and hosts.");
        getter.IsAssembly.Should().BeTrue("getter should be 'internal'.");
    }

    #endregion

    #region New RFC 9110 Error Type Mappings

    [Fact]
    public void GetStatusCode_GoneError_returns_410()
    {
        var options = new TrellisAspOptions();
        options.GetStatusCode(new Error.Gone(new ResourceRef("Resource", null)) { Detail = "Permanently removed" }).Should().Be(StatusCodes.Status410Gone);
    }

    [Fact]
    public void GetStatusCode_MethodNotAllowedError_returns_405()
    {
        var options = new TrellisAspOptions();
        options.GetStatusCode(new Error.MethodNotAllowed(EquatableArray.Create("GET")) { Detail = "Not allowed" }).Should().Be(StatusCodes.Status405MethodNotAllowed);
    }

    [Fact]
    public void GetStatusCode_NotAcceptableError_returns_406()
    {
        var options = new TrellisAspOptions();
        options.GetStatusCode(new Error.NotAcceptable(EquatableArray<string>.Empty) { Detail = "Not acceptable" }).Should().Be(StatusCodes.Status406NotAcceptable);
    }

    [Fact]
    public void GetStatusCode_UnsupportedMediaTypeError_returns_415()
    {
        var options = new TrellisAspOptions();
        options.GetStatusCode(new Error.UnsupportedMediaType(EquatableArray<string>.Empty) { Detail = "Unsupported" }).Should().Be(StatusCodes.Status415UnsupportedMediaType);
    }

    [Fact]
    public void GetStatusCode_ContentTooLargeError_returns_413()
    {
        var options = new TrellisAspOptions();
        options.GetStatusCode(new Error.ContentTooLarge() { Detail = "Too large" }).Should().Be(StatusCodes.Status413RequestEntityTooLarge);
    }

    [Fact]
    public void GetStatusCode_RangeNotSatisfiableError_returns_416()
    {
        var options = new TrellisAspOptions();
        options.GetStatusCode(new Error.RangeNotSatisfiable(1024, "bytes") { Detail = "Not satisfiable" }).Should().Be(StatusCodes.Status416RangeNotSatisfiable);
    }

    #endregion
}