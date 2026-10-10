namespace Trellis.Asp.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Primitives;

public sealed class PrimitiveValidationExamplesTests
{
    [Fact]
    public async Task ScalarValidation_Mvc_GuidEmpty_ReturnsDocumentedProblem()
    {
        await using var app = await CreateAppAsync();
        using var client = app.GetTestClient();
        const string path = "/rfc-examples/customers/00000000-0000-0000-0000-000000000000";

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, path, "id", "value.not-default",
            "Customer Id cannot be Guid.Empty.", """{"in":"path","name":"id"}""");
    }

    [Fact]
    public async Task ScalarValidation_Mvc_EmptyName_ReturnsDocumentedProblem()
    {
        await using var app = await CreateAppAsync();
        using var client = app.GetTestClient();
        const string path = "/rfc-examples/users";

        using var response = await client.PostAsJsonAsync(
            path, new CreateUserRequest("", "Doe", "test@example.com"), TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, path, "firstName", "value.not-empty",
            "First Name cannot be empty.", """{"in":"body","pointer":"/firstName"}""");
    }

    [Fact]
    public async Task ToHttpResponse_MinimalApi_EmptyName_ReturnsDocumentedProblem()
    {
        await using var app = await CreateAppAsync();
        using var client = app.GetTestClient();
        const string path = "/rfc-examples/manual/users";

        using var response = await client.PostAsJsonAsync(
            path, new CreateUserRequest("", "Doe", "test@example.com"), TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, path, "firstName", "value.not-empty",
            "First Name cannot be empty.", """{"in":"body","pointer":"/firstName"}""");
    }

    [Fact]
    public async Task ToHttpResponseAsync_MinimalApi_InvalidEmail_ReturnsDocumentedProblem()
    {
        await using var app = await CreateAppAsync();
        using var client = app.GetTestClient();
        const string path = "/rfc-examples/register";

        using var response = await client.PostAsJsonAsync(
            path, new RegisterUserRequest("invalid", "example"), TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, path, "email", "string.email",
            "Email address is not valid.", """{"in":"body","pointer":"/email"}""");
    }

    private static async Task<WebApplication> CreateAppAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddTrellisAsp();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(PrimitiveExamplesCustomersController).Assembly)
            .AddScalarValueValidation();

        var app = builder.Build();
        app.UseScalarValueValidation();
        app.MapControllers();
        app.MapPost("/rfc-examples/manual/users", (CreateUserRequest request) =>
            FirstName.TryCreate(request.FirstName, nameof(request.FirstName))
                .Combine(LastName.TryCreate(request.LastName, nameof(request.LastName)))
                .Combine(EmailAddress.TryCreate(request.Email, nameof(request.Email)))
                .Bind((first, last, email) => Result.Ok(new RegisterUserDto(first, last, email)))
                .ToHttpResponse());
        app.MapPost("/rfc-examples/register", (RegisterUserRequest request) =>
            EmailAddress.TryCreate(request.Email, nameof(request.Email))
                .BindAsync(static email => Task.FromResult(Result.Ok(email)))
                .ToHttpResponseAsync());

        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response, string instance, string field, string code, string detail, string location)
    {
        response.StatusCode.Should().Be((HttpStatusCode)422);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var actual = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().BeOfType<JsonObject>().Which;
        actual.Remove("traceId");
        var expected = JsonNode.Parse($$"""
            {
              "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
              "title": "One or more validation errors occurred.",
              "status": 422,
              "instance": "{{instance}}",
              "errors": { "{{field}}": ["{{detail}}"] },
              "code": "error.unspecified",
              "kind": "unprocessable-content",
              "fieldViolations": [{
                "code": "{{code}}",
                "detail": "{{detail}}",
                "location": {{location}}
              }]
            }
            """);

        JsonNode.DeepEquals(actual, expected).Should().BeTrue($"the documented payload must match the response: {actual}");
    }

    public sealed record CreateUserRequest(string FirstName, string LastName, string Email);
    public sealed record RegisterUserRequest(string Email, string Password);
    public sealed record RegisterUserDto(FirstName FirstName, LastName LastName, EmailAddress Email);
}

[NotDefault]
public sealed partial class CustomerId : RequiredGuid<CustomerId>;

[Trim, NotDefault]
public sealed partial class FirstName : RequiredString<FirstName>;

[Trim, NotDefault]
public sealed partial class LastName : RequiredString<LastName>;

[ApiController]
[Route("rfc-examples/customers")]
public sealed class PrimitiveExamplesCustomersController : ControllerBase
{
    [HttpGet("{id}")]
    public ActionResult<CustomerId> Get(CustomerId id) => Ok(id);
}

[ApiController]
[Route("rfc-examples/users")]
public sealed class PrimitiveExamplesUsersController : ControllerBase
{
    [HttpPost]
    public IActionResult Register(PrimitiveValidationExamplesTests.RegisterUserDto dto) => Ok(dto);
}