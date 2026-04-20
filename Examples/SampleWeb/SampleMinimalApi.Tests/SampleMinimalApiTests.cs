namespace SampleMinimalApi.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SampleMinimalApi.Models;
using SampleUserLibrary;
using Trellis;
using Trellis.Asp.Validation;
using Trellis.Primitives;

/// <summary>
/// Black-box integration tests over the Minimal API. Each test verifies one piece of the
/// "AI-generated gold standard" surface: routing by VO, 422 with FieldViolations on domain
/// validation failure, 404 on miss, and the workflow commit boundary (payment fired exactly
/// once on order confirmation).
/// </summary>
public class SampleMinimalApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions s_json = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        // Mirror the API's JSON pipeline so the client side understands the same VO and
        // Maybe<T> shapes. In real client code these factories come from Trellis.Asp.
        options.Converters.Add(new ValidatingJsonConverterFactory());
        options.Converters.Add(new MaybeScalarValueJsonConverterFactory());
        return options;
    }

    private readonly WebApplicationFactory<Program> _factory;

    public SampleMinimalApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_user_returns_201_with_user_response()
    {
        using var client = _factory.CreateClient();

        var dto = ValidUserDto();
        using var response = await client.PostAsJsonAsync(new Uri("/users", UriKind.Relative), dto, s_json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<UserResponse>(s_json, Ct);
        body.Should().NotBeNull();
        body!.Email.Value.Should().Be(dto.Email.Value);

        // Round-trip through GET via the Location header.
        response.Headers.Location.Should().NotBeNull();
        using var fetched = await client.GetAsync(response.Headers.Location, Ct);
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_user_under_age_returns_422_with_field_violation()
    {
        using var client = _factory.CreateClient();

        var dto = ValidUserDto() with { Age = Age.Create(15) };
        using var response = await client.PostAsJsonAsync(new Uri("/users", UriKind.Relative), dto, s_json, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(s_json, Ct);
        problem.GetProperty("status").GetInt32().Should().Be(422);
        problem.GetProperty("errors").GetProperty("Age").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Get_unknown_user_returns_404()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri($"/users/{Guid.NewGuid()}", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_and_list_products_round_trips_with_in_stock_filter()
    {
        using var client = _factory.CreateClient();

        await CreateProduct(client, "Widget", 10m, 5);
        await CreateProduct(client, "OutOfStock", 99m, 0);

        using var response = await client.GetAsync(new Uri("/products?inStock=true", UriKind.Relative), Ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<List<ProductResponse>>(s_json, Ct);
        body.Should().NotBeNull();
        body!.Should().OnlyContain(p => p.StockQuantity > 0);
        body.Should().Contain(p => p.Name.Value == "Widget");
        body.Should().NotContain(p => p.Name.Value == "OutOfStock");
    }

    [Fact]
    public async Task Order_lifecycle_from_create_to_ship_round_trips()
    {
        var payments = new RecordingPaymentService();
        using var client = CreateClientWith(payments);

        var product = await CreateProduct(client, "Gadget", 25m, 10);
        var customerId = CustomerId.Create(Guid.NewGuid());

        var order = await PostJson<OrderResponse>(client, "/orders", new CreateOrderRequest { CustomerId = customerId });
        order.State.Should().Be(OrderState.Draft);

        await PostJson<OrderResponse>(client, $"/orders/{order.Id.Value}/lines",
            new AddLineDto { ProductId = product.Id, Quantity = 2 });

        var confirmed = await PostJson<OrderResponse>(client, $"/orders/{order.Id.Value}/confirm", payload: null);
        confirmed.State.Should().Be(OrderState.Confirmed);
        confirmed.Total.Value.Should().Be(50m);

        var shipped = await PostJson<OrderResponse>(client, $"/orders/{order.Id.Value}/ship", payload: null);
        shipped.State.Should().Be(OrderState.Shipped);

        // Commit-boundary assertion: confirming the order MUST charge the payment service
        // exactly once. If an endpoint mutated the aggregate directly (bypassing the workflow),
        // this counter would be zero — the test that catches A10 violations.
        payments.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Confirm_empty_order_returns_422()
    {
        using var client = _factory.CreateClient();
        var order = await PostJson<OrderResponse>(client, "/orders",
            new CreateOrderRequest { CustomerId = CustomerId.Create(Guid.NewGuid()) });

        using var response = await client.PostAsync(
            new Uri($"/orders/{order.Id.Value}/confirm", UriKind.Relative),
            content: null,
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);
    }

    private HttpClient CreateClientWith(IPaymentService payments) =>
        _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IPaymentService>();
            s.AddSingleton(payments);
        })).CreateClient();

    private static async Task<ProductResponse> CreateProduct(HttpClient client, string name, decimal price, int stock)
    {
        var dto = new CreateProductDto
        {
            Name = ProductName.Create(name),
            Price = MonetaryAmount.Create(price),
            StockQuantity = stock,
        };
        return await PostJson<ProductResponse>(client, "/products", dto);
    }

    private static async Task<T> PostJson<T>(HttpClient client, string path, object? payload)
    {
        using var response = payload is null
            ? await client.PostAsync(new Uri(path, UriKind.Relative), content: null, Ct)
            : await client.PostAsJsonAsync(new Uri(path, UriKind.Relative), payload, s_json, Ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<T>(s_json, Ct);
        body.Should().NotBeNull();
        return body!;
    }

    private static RegisterUserDto ValidUserDto() => new()
    {
        FirstName = FirstName.Create("Ada"),
        LastName = LastName.Create("Lovelace"),
        Email = EmailAddress.Create("ada@example.com"),
        Phone = PhoneNumber.Create("+15555550123"),
        Age = Age.Create(36),
        Country = CountryCode.Create("GB"),
        Password = "Strong!Pass123",
    };
}

/// <summary>
/// Recording double for <see cref="IPaymentService"/>. Used to assert that
/// <c>OrderWorkflow.ConfirmAsync</c> calls payment exactly once per confirmation.
/// </summary>
internal sealed class RecordingPaymentService : IPaymentService
{
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);

    public Task<Result<string>> ProcessPaymentAsync(OrderId orderId, decimal amount, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(Result.Ok($"PAY-{orderId.Value:N}"[..16]));
    }

    public Task<Result> RefundPaymentAsync(string paymentReference, CancellationToken ct = default) =>
        Task.FromResult(Result.Ok());
}
