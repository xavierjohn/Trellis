namespace Trellis.Showcase.MinimalApi.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Trellis.Primitives;
using Trellis.Showcase.Application;
using Trellis.Showcase.Application.Models;
using Trellis.Showcase.MinimalApi;

/// <summary>
/// Black-box integration tests over the Showcase Minimal API host. Mirrors
/// <c>Trellis.Showcase.Tests.Api.ShowcaseApiTests</c> verbatim — proves that the same DTOs,
/// repository, and <c>BankingWorkflow</c> produce identical HTTP behaviour across hosting styles.
/// </summary>
public class ShowcaseMinimalApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
    private readonly WebApplicationFactory<Program> _factory;

    public ShowcaseMinimalApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_unknown_account_returns_404_problem_details()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri($"/api/accounts/{Guid.NewGuid()}", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_seeded_account_returns_account_response()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri($"/api/accounts/{ShowcaseSeed.AliceCheckingId.Value}", UriKind.Relative), Ct);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>(JsonOptions, Ct);
        body.Should().NotBeNull();
        body!.Status.Should().Be(Trellis.Showcase.Domain.Aggregates.AccountStatus.Active);
    }

    [Fact]
    public async Task Deposit_with_zero_amount_returns_422()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/accounts/{ShowcaseSeed.AliceCheckingId.Value}/deposit", UriKind.Relative),
            new DepositRequest(Money.Create(0m, "USD")),
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);
    }

    [Fact]
    public async Task Secure_withdraw_with_invalid_code_returns_422()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/accounts/{ShowcaseSeed.AliceCheckingId.Value}/secure-withdraw", UriKind.Relative),
            new SecureWithdrawRequest(Money.Create(2000m, "USD"), VerificationCode: "abc"),
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);
    }

    [Fact]
    public async Task Diagnostics_fault_returns_500_with_fault_id()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/api/diagnostics/fault", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Unfreeze_active_account_returns_409_conflict_from_state_machine()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync(
            new Uri($"/api/accounts/{ShowcaseSeed.BobCheckingId.Value}/unfreeze", UriKind.Relative),
            content: null,
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Open_account_with_string_enum_payload_returns_201()
    {
        // Mirrors api.http: AccountType is sent as a string ("Checking"), not a number.
        // Requires JsonStringEnumConverter to be registered globally.
        var client = _factory.CreateClient();
        var json = """
            {
              "customerId": "11111111-1111-1111-1111-111111111111",
              "accountType": "Checking",
              "initialDeposit":       { "amount": 250.00, "currency": "USD" },
              "dailyWithdrawalLimit": { "amount": 500.00, "currency": "USD" },
              "overdraftLimit":       { "amount":   0.00, "currency": "USD" }
            }
            """;
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            new Uri("/api/accounts", UriKind.Relative),
            content,
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
