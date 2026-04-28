namespace Trellis.Asp.Tests;

using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

/// <summary>
/// Integration tests that verify <see cref="ServiceCollectionExtensions.AddTrellisAsp(IServiceCollection)"/>
/// fully wires the MVC pipeline for scalar value object validation and <see cref="Maybe{T}"/> properties.
///
/// Recipe 14 (cookbook) and the public docs claim that <c>AddTrellisAsp()</c> alone is the only
/// wiring required for controllers to accept <c>Maybe&lt;TScalar&gt;</c> request properties.
/// Prior to the fix in this commit, only the JSON converter was registered — the MVC-side
/// <see cref="MaybeSuppressChildValidationMetadataProvider"/>, model binder provider, and validation
/// filter were not — so <c>ValidationVisitor</c> would reflectively access <c>Maybe&lt;T&gt;.Value</c>
/// on a <c>None</c> instance and throw <see cref="System.InvalidOperationException"/> ("Maybe has no value")
/// before the action ran, surfacing as HTTP 500.
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
                    s.AddTrellisAsp();
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
    public async Task AddTrellisAsp_with_controllers_accepts_DTO_with_omitted_Maybe_scalar_property()
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();

        // Email is required, phone is Maybe<Phone>. Omit phone entirely.
        var json = """{"email":"a@b.com"}""";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var resp = await client.PostAsync("/maybe-dto", content, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "AddTrellisAsp() must register MaybeSuppressChildValidationMetadataProvider so MVC's " +
            "ValidationVisitor does not reflectively access Maybe<T>.Value on a None instance");
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("\"hasPhone\":false");
        body.Should().Contain("\"email\":\"a@b.com\"");
    }

    [Fact]
    public async Task AddTrellisAsp_with_controllers_accepts_DTO_with_explicit_null_Maybe_scalar_property()
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
    public async Task AddTrellisAsp_with_controllers_accepts_DTO_with_present_Maybe_scalar_property()
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
    public void AddTrellisAsp_registers_MaybeSuppressChildValidationMetadataProvider()
    {
        var services = new ServiceCollection();
        services.AddTrellisAsp();
        services.AddControllers();

        var sp = services.BuildServiceProvider();
        var mvcOptions = sp.GetRequiredService<IOptions<MvcOptions>>().Value;

        mvcOptions.ModelMetadataDetailsProviders
            .Any(p => p is MaybeSuppressChildValidationMetadataProvider)
            .Should().BeTrue(
                "Recipe 14 documents AddTrellisAsp() as the one-call setup for Maybe<TScalar> on DTOs");
    }

    [Fact]
    public void AddTrellisAsp_registers_ScalarValueModelBinderProvider_at_front()
    {
        var services = new ServiceCollection();
        services.AddTrellisAsp();
        services.AddControllers();

        var sp = services.BuildServiceProvider();
        var mvcOptions = sp.GetRequiredService<IOptions<MvcOptions>>().Value;

        mvcOptions.ModelBinderProviders.FirstOrDefault()
            .Should().BeOfType<ScalarValueModelBinderProvider>(
                "MaybeModelBinder is provided by ScalarValueModelBinderProvider for route/query/header bindings");
    }

    [Fact]
    public void AddTrellisAsp_registers_ScalarValueValidationFilter()
    {
        var services = new ServiceCollection();
        services.AddTrellisAsp();
        services.AddControllers();

        var sp = services.BuildServiceProvider();
        var mvcOptions = sp.GetRequiredService<IOptions<MvcOptions>>().Value;

        mvcOptions.Filters
            .Any(f => f is TypeFilterAttribute tfa && tfa.ImplementationType == typeof(ScalarValueValidationFilter))
            .Should().BeTrue();
    }

    [Fact]
    public void AddTrellisAsp_suppresses_ModelStateInvalidFilter()
    {
        var services = new ServiceCollection();
        services.AddTrellisAsp();
        services.AddControllers();

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value
            .SuppressModelStateInvalidFilter.Should().BeTrue();
    }

    #endregion
}

#region Test fixtures (controller, DTO, scalar VOs)

public sealed class TestPhone : ScalarValueObject<TestPhone, string>, IScalarValue<TestPhone, string>
{
    private TestPhone(string value) : base(value) { }

    public static Result<TestPhone> TryCreate(string? value, string? fieldName = null)
    {
        var field = fieldName ?? "phone";
        return string.IsNullOrWhiteSpace(value)
            ? Result.Fail<TestPhone>(new Error.UnprocessableContent(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Phone required." })))
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
            return Result.Fail<TestEmail2>(new Error.UnprocessableContent(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Email required." })));
        if (!value.Contains('@'))
            return Result.Fail<TestEmail2>(new Error.UnprocessableContent(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Email must contain @." })));
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