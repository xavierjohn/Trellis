namespace Trellis.Asp.Tests;

using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Trellis;
using Trellis.Asp.ModelBinding;
using Trellis.Asp.Validation;
using Trellis.Primitives;

/// <summary>
/// Integration tests that verify <see cref="ServiceCollectionExtensions.AddTrellisAspWithScalarValidation(IServiceCollection)"/>
/// fully wires the MVC pipeline for scalar value object validation and <see cref="Maybe{T}"/> properties.
///
/// Recipe 14 (cookbook) and the public docs claim that <c>AddTrellisAspWithScalarValidation()</c> alone is the only
/// wiring required for controllers to accept <c>Maybe&lt;TScalar&gt;</c> request properties. Without
/// the <see cref="MaybeSuppressChildValidationMetadataProvider"/>, model binder provider, and validation
/// filter that <c>AddScalarValueValidation()</c> contributes, <c>ValidationVisitor</c> would
/// reflectively access <c>Maybe&lt;T&gt;.Value</c> on a <c>None</c> instance and throw
/// <see cref="System.InvalidOperationException"/> ("Maybe has no value") before the action ran,
/// surfacing as HTTP 500.
/// </summary>
public sealed class AddTrellisAspMvcIntegrationTests
{
    private static IHost CreateHost()
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddProblemDetails();
                    s.AddTrellisAspWithScalarValidation();
                    s.AddControllers().AddApplicationPart(typeof(MaybeDtoController).Assembly);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return builder.Start();
    }

    [Fact]
    public async Task AddTrellisAspWithScalarValidation_with_controllers_accepts_DTO_with_omitted_Maybe_scalar_property()
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();

        // Email is required, phone is Maybe<Phone>. Omit phone entirely.
        var json = """{"email":"a@b.com"}""";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var resp = await client.PostAsync("/maybe-dto", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "AddTrellisAspWithScalarValidation() must register MaybeSuppressChildValidationMetadataProvider so MVC's " +
            "ValidationVisitor does not reflectively access Maybe<T>.Value on a None instance");
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("\"hasPhone\":false");
        body.Should().Contain("\"email\":\"a@b.com\"");
    }

    [Fact]
    public async Task AddTrellisAspWithScalarValidation_with_controllers_accepts_DTO_with_explicit_null_Maybe_scalar_property()
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();

        var json = """{"email":"a@b.com","phone":null}""";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var resp = await client.PostAsync("/maybe-dto", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("\"hasPhone\":false");
    }

    [Fact]
    public async Task AddTrellisAspWithScalarValidation_with_controllers_accepts_DTO_with_present_Maybe_scalar_property()
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();

        var json = """{"email":"a@b.com","phone":"+15551234567"}""";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var resp = await client.PostAsync("/maybe-dto", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("\"hasPhone\":true");
        body.Should().Contain("\"phone\":\"+15551234567\"");
    }

    #region Unit-level registration assertions

    [Fact]
    public void AddTrellisAspWithScalarValidation_registers_MaybeSuppressChildValidationMetadataProvider()
    {
        var services = new ServiceCollection();
        services.AddTrellisAspWithScalarValidation();
        services.AddControllers();

        var sp = services.BuildServiceProvider();
        var mvcOptions = sp.GetRequiredService<IOptions<MvcOptions>>().Value;

        mvcOptions.ModelMetadataDetailsProviders
            .Any(p => p is MaybeSuppressChildValidationMetadataProvider)
            .Should().BeTrue(
                "Recipe 14 documents AddTrellisAspWithScalarValidation() as the one-call setup for Maybe<TScalar> on DTOs");
    }

    [Fact]
    public void AddTrellisAspWithScalarValidation_registers_ScalarValueModelBinderProvider_at_front()
    {
        var services = new ServiceCollection();
        services.AddTrellisAspWithScalarValidation();
        services.AddControllers();

        var sp = services.BuildServiceProvider();
        var mvcOptions = sp.GetRequiredService<IOptions<MvcOptions>>().Value;

        mvcOptions.ModelBinderProviders.FirstOrDefault()
            .Should().BeOfType<ScalarValueModelBinderProvider>(
                "MaybeModelBinder is provided by ScalarValueModelBinderProvider for route/query/header bindings");
    }

    [Fact]
    public void AddTrellisAspWithScalarValidation_registers_ScalarValueValidationFilter()
    {
        var services = new ServiceCollection();
        services.AddTrellisAspWithScalarValidation();
        services.AddControllers();

        var sp = services.BuildServiceProvider();
        var mvcOptions = sp.GetRequiredService<IOptions<MvcOptions>>().Value;

        mvcOptions.Filters
            .Any(f => f is TypeFilterAttribute tfa && tfa.ImplementationType == typeof(ScalarValueValidationFilter))
            .Should().BeTrue();
    }

    [Fact]
    public void AddTrellisAspWithScalarValidation_suppresses_ModelStateInvalidFilter()
    {
        var services = new ServiceCollection();
        services.AddTrellisAspWithScalarValidation();
        services.AddControllers();

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value
            .SuppressModelStateInvalidFilter.Should().BeTrue();
    }

    [Fact]
    public void AddTrellisAsp_alone_does_NOT_register_scalar_validation_infrastructure()
    {
        // Verifies the explicit-opt-in contract: AddTrellisAsp() registers error mapping
        // and the resource collection name registry but does NOT mutate MvcOptions /
        // JsonOptions. Hosts that only need error mapping must NOT silently inherit the
        // global model-binder / converter / filter wiring.
        var services = new ServiceCollection();
        services.AddTrellisAsp();
        services.AddControllers();

        var sp = services.BuildServiceProvider();
        var mvcOptions = sp.GetRequiredService<IOptions<MvcOptions>>().Value;

        mvcOptions.ModelBinderProviders.OfType<ScalarValueModelBinderProvider>()
            .Should().BeEmpty("AddTrellisAsp() must not register the scalar value model binder provider on its own");

        mvcOptions.ModelMetadataDetailsProviders.OfType<MaybeSuppressChildValidationMetadataProvider>()
            .Should().BeEmpty("AddTrellisAsp() must not register the Maybe metadata provider on its own");

        mvcOptions.Filters
            .Any(f => f is TypeFilterAttribute tfa && tfa.ImplementationType == typeof(ScalarValueValidationFilter))
            .Should().BeFalse("AddTrellisAsp() must not register the scalar value validation filter on its own");

        sp.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value
            .SuppressModelStateInvalidFilter.Should().BeFalse(
                "AddTrellisAsp() must not flip the model-state-invalid filter suppression — scalar validation owns that toggle");
    }

    [Fact]
    public async Task AddTrellisAspWithScalarValidation_with_controllers_composite_VO_failure_omits_phantom_parameter_entry()
    {
        // When a composite value object inside a [FromBody] request fails multi-field validation,
        // the wire response must contain ONLY the per-field errors emitted by the converter.
        // It must NOT include an extra entry keyed by the action parameter name (e.g., "request":
        // ["The request field is required."]) — that entry comes from MVC's binding pipeline
        // observing a null body parameter after the deserializer short-circuited, and it
        // duplicates the same logical "input is bad" condition under a key the client cannot
        // act on.
        using var host = CreateHost();
        using var client = host.GetTestClient();

        // All three required string fields empty — composite TryCreate combines three
        // FieldViolations into a single Error.InvalidInput.
        var json = """{"address":{"street":"","city":"","state":""}}""";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var resp = await client.PostAsync("/composite-dto", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var bodyText = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(bodyText);
        var errors = doc.RootElement.GetProperty("errors");

        // The structured branch from CompositeValueObjectJsonConverter must surface per-leaf entries
        // keyed by `<parentPath>.<leaf>` in MVC dot+bracket convention.
        errors.TryGetProperty("address.street", out _).Should().BeTrue("per-leaf street error must be present");
        errors.TryGetProperty("address.city", out _).Should().BeTrue("per-leaf city error must be present");
        errors.TryGetProperty("address.state", out _).Should().BeTrue("per-leaf state error must be present");

        // Old collapsed shape must be gone — `$.address` (or just `address`) carrying all
        // leaf reasons joined into one string was the regression PR #474 introduced for the
        // Minimal API path; this fix extends the per-leaf expansion to the MVC path.
        errors.TryGetProperty("$.address", out _).Should().BeFalse(
            "the old joined-leaves collapsed key must not appear");
        errors.TryGetProperty("address", out _).Should().BeFalse(
            "the old joined-leaves collapsed key must not appear under any casing");

        // The phantom parameter-name entry must be gone.
        errors.TryGetProperty("request", out _).Should().BeFalse(
            "MVC's null-body-parameter ModelState entry must not appear alongside the structured per-field errors");
    }

    [Fact]
    public async Task AddTrellisAspWithScalarValidation_with_controllers_unstructured_composite_VO_failure_surfaces_curated_message()
    {
        // The composite-VO converter throws unstructured TrellisJsonValidationException
        // (no UnprocessableContent) for shape/format mismatches — e.g. when the JSON
        // value is not an object. Setting AllowInputFormatterExceptionMessages = false
        // preserves the exception, but ModelStateDictionary stores an empty ErrorMessage
        // for non-InputFormatterException entries; without dedicated handling, the
        // curated converter message would be lost. The filter must surface tjx.Message
        // under the JSON-path key for these cases too.
        using var host = CreateHost();
        using var client = host.GetTestClient();

        // "address" is a string instead of an object — converter throws
        // "Expected JSON object for TestAddress value." with no UnprocessableContent.
        var json = """{"address":"not-an-object"}""";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var resp = await client.PostAsync("/composite-dto", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var bodyText = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(bodyText);
        var errors = doc.RootElement.GetProperty("errors");

        errors.TryGetProperty("address", out var addressErrors).Should().BeTrue(
            "the unstructured Trellis JSON validation exception's curated message must surface under the JSON path key");
        addressErrors.GetArrayLength().Should().BeGreaterThan(0);
        addressErrors[0].GetString().Should().Contain("TestAddress");

        errors.TryGetProperty("request", out _).Should().BeFalse(
            "the phantom body-parameter entry must still be removed even on the unstructured path");
    }

    [Fact]
    public async Task AddTrellisAspWithScalarValidation_with_controllers_preserves_unrelated_required_errors_alongside_composite_VO_failure()
    {
        // The phantom-entry filter must NOT drop legitimate "is required" errors from
        // unrelated ModelState entries — e.g. a missing required query parameter. Only
        // the entry whose key matches a [FromBody] parameter name is the phantom; every
        // other required-error must be carried forward to the response.
        using var host = CreateHost();
        using var client = host.GetTestClient();

        // Body fails composite VO; "tenant" query parameter is omitted.
        var json = """{"address":{"street":"","city":"","state":""}}""";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var resp = await client.PostAsync("/composite-dto-with-query", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var bodyText = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(bodyText);
        var errors = doc.RootElement.GetProperty("errors");

        errors.TryGetProperty("address.street", out _).Should().BeTrue();
        errors.TryGetProperty("tenant", out _).Should().BeTrue(
            "the missing query-parameter required-error must be preserved — only the phantom body-parameter entry should be filtered out");
        errors.TryGetProperty("request", out _).Should().BeFalse();
    }

    #endregion
}

#region Composite VO test fixture (regression guard for phantom parameter-name entry)

[JsonConverter(typeof(CompositeValueObjectJsonConverter<TestAddress>))]
public sealed class TestAddress : ValueObject
{
    public string Street { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string State { get; private set; } = string.Empty;

    private TestAddress() { }

    private TestAddress(string street, string city, string state)
    {
        Street = street; City = city; State = state;
    }

    public static Result<TestAddress> TryCreate(string street, string city, string state, string? fieldName = null)
    {
        var violations = new System.Collections.Generic.List<FieldViolation>();
        if (string.IsNullOrWhiteSpace(street))
            violations.Add(new FieldViolation(InputPointer.ForProperty("street"), ValidationCodes.Unspecified) { Detail = "Street is required." });
        if (string.IsNullOrWhiteSpace(city))
            violations.Add(new FieldViolation(InputPointer.ForProperty("city"), ValidationCodes.Unspecified) { Detail = "City is required." });
        if (string.IsNullOrWhiteSpace(state))
            violations.Add(new FieldViolation(InputPointer.ForProperty("state"), ValidationCodes.Unspecified) { Detail = "State is required." });

        return violations.Count > 0
            ? Result.Fail<TestAddress>(new Error.InvalidInput(EquatableArray.Create(violations.ToArray())))
            : Result.Ok(new TestAddress(street, city, state));
    }

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Street);
        components.Add(City);
        components.Add(State);
    }
}

public sealed record CompositeDtoRequest
{
    public TestAddress Address { get; init; } = null!;
}

[ApiController]
[Route("composite-dto")]
public sealed class CompositeDtoController : ControllerBase
{
    [HttpPost]
    public IActionResult Post([FromBody] CompositeDtoRequest request) => Ok(new { ok = true });
}

[ApiController]
[Route("composite-dto-with-query")]
public sealed class CompositeDtoWithQueryController : ControllerBase
{
    [HttpPost]
    public IActionResult Post(
        [FromQuery, BindRequired] string tenant,
        [FromBody] CompositeDtoRequest request) => Ok(new { ok = true, tenant });
}

public sealed class TestPhone : ScalarValueObject<TestPhone, string>, IScalarValue<TestPhone, string>
{
    private TestPhone(string value) : base(value) { }

    public static Result<TestPhone> TryCreate(string? value, string? fieldName = null)
    {
        var field = fieldName ?? "phone";
        return string.IsNullOrWhiteSpace(value)
            ? Result.Fail<TestPhone>(new Error.InvalidInput(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty(field), ValidationCodes.Unspecified) { Detail = "Phone required." })))
            : Result.Ok(new TestPhone(value));
    }
}

public sealed class TestEmail2 : ScalarValueObject<TestEmail2, string>, IScalarValue<TestEmail2, string>
{
    private TestEmail2(string value) : base(value) { }

    public static Result<TestEmail2> TryCreate(string? value, string? fieldName = null)
    {
        var field = fieldName ?? "email";
        if (string.IsNullOrWhiteSpace(value))
            return Result.Fail<TestEmail2>(new Error.InvalidInput(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty(field), ValidationCodes.Unspecified) { Detail = "Email required." })));
        if (!value.Contains('@'))
            return Result.Fail<TestEmail2>(new Error.InvalidInput(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty(field), ValidationCodes.Unspecified) { Detail = "Email must contain @." })));
        return Result.Ok(new TestEmail2(value));
    }
}

public sealed record MaybeDtoRequest
{
    public TestEmail2 Email { get; init; } = null!;
    public Maybe<TestPhone> Phone { get; init; }
}

[ApiController]
[Route("maybe-dto")]
public sealed class MaybeDtoController : ControllerBase
{
    [HttpPost]
    public IActionResult Post([FromBody] MaybeDtoRequest request) =>
        Ok(new
        {
            email = request.Email.Value,
            hasPhone = request.Phone.HasValue,
            phone = request.Phone.HasValue ? request.Phone.Value.Value : null,
        });
}

#endregion