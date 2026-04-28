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
    public async Task Unfreeze_active_account_returns_422_unprocessable_from_state_machine()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync(
            new Uri($"/api/accounts/{ShowcaseSeed.BobCheckingId.Value}/unfreeze", UriKind.Relative),
            content: null,
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
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

    [Fact]
    public async Task Open_account_with_missing_body_properties_returns_400_not_500()
    {
        var client = _factory.CreateClient();
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            new Uri("/api/accounts", UriKind.Relative),
            content,
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Open_account_with_negative_money_surfaces_validation_message()
    {
        // Locks the framework fix: TrellisJsonValidationException thrown from MoneyJsonConverter
        // (e.g. "Amount cannot be negative") MUST be surfaced in the Problem Details body so callers
        // can see *why* their request was rejected. Previously Minimal API returned the generic
        // "The request body contains invalid JSON." with no actionable info.
        var client = _factory.CreateClient();
        var json = """
            {
              "customerId": "11111111-1111-1111-1111-111111111111",
              "accountType": "Checking",
              "initialDeposit":       { "amount": -100.00, "currency": "USD" },
              "dailyWithdrawalLimit": { "amount":  500.00, "currency": "USD" },
              "overdraftLimit":       { "amount":    0.00, "currency": "USD" }
            }
            """;
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            new Uri("/api/accounts", UriKind.Relative),
            content,
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().NotContain("The request body contains invalid JSON",
            "the framework should surface the curated TrellisJsonValidationException message instead of the generic placeholder");
        body.Should().Contain("negative", "the Money converter's curated message must reach the client");
    }

    private sealed record PageEnvelope(
        IReadOnlyList<AccountResponse> Items,
        PageLinkDto? Next,
        PageLinkDto? Previous,
        int RequestedLimit,
        int AppliedLimit,
        int DeliveredCount,
        bool WasCapped);

    private sealed record PageLinkDto(string Cursor, string Href);

    [Fact]
    public async Task Paginated_list_caps_at_5_and_emits_next_cursor_plus_link_header()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/api/accounts/?limit=10", UriKind.Relative), Ct);

        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<PageEnvelope>(JsonOptions, Ct);
        page.Should().NotBeNull();
        page!.Items.Should().HaveCount(5);
        page.RequestedLimit.Should().Be(10);
        page.AppliedLimit.Should().Be(5);
        page.WasCapped.Should().BeTrue();
        page.DeliveredCount.Should().Be(5);
        page.Next.Should().NotBeNull();
        page.Next!.Cursor.Should().NotBeNullOrEmpty();

        response.Headers.Should().ContainKey("Link");
        var link = response.Headers.GetValues("Link").Single();
        link.Should().Contain("rel=\"next\"");
        link.Should().Contain($"cursor={page.Next.Cursor}");
    }

    [Fact]
    public async Task Following_next_link_returns_subsequent_distinct_page()
    {
        var client = _factory.CreateClient();
        var firstResp = await client.GetAsync(new Uri("/api/accounts/?limit=5", UriKind.Relative), Ct);
        var first = await firstResp.Content.ReadFromJsonAsync<PageEnvelope>(JsonOptions, Ct);
        first!.Next.Should().NotBeNull();

        var secondResp = await client.GetAsync(new Uri(first.Next!.Href), Ct);
        secondResp.EnsureSuccessStatusCode();
        var second = await secondResp.Content.ReadFromJsonAsync<PageEnvelope>(JsonOptions, Ct);

        second!.Items.Should().NotBeEmpty();
        var firstIds = first.Items.Select(a => a.Id).ToHashSet();
        var secondIds = second.Items.Select(a => a.Id).ToHashSet();
        firstIds.Overlaps(secondIds).Should().BeFalse("subsequent pages must contain distinct items");
    }

    [Fact]
    public async Task Drain_to_last_page_returns_no_next_link_or_header()
    {
        var client = _factory.CreateClient();
        var url = "/api/accounts/?limit=5";
        PageEnvelope? page = null;
        HttpResponseMessage? lastResp = null;
        for (int i = 0; i < 10; i++)
        {
            lastResp = await client.GetAsync(new Uri(url, UriKind.RelativeOrAbsolute), Ct);
            lastResp.EnsureSuccessStatusCode();
            page = await lastResp.Content.ReadFromJsonAsync<PageEnvelope>(JsonOptions, Ct);
            if (page!.Next is null) break;
            url = page.Next.Href;
        }

        page!.Next.Should().BeNull("after draining all pages, next must be absent");
        lastResp!.Headers.Contains("Link").Should().BeFalse("last page must not emit a Link header");
    }

    [Fact]
    public async Task Malformed_cursor_returns_400_problem_details()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/api/accounts/?cursor=not-a-real-cursor", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Limit_zero_defaults_to_ten_and_caps_to_five()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/api/accounts/?limit=0", UriKind.Relative), Ct);

        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<PageEnvelope>(JsonOptions, Ct);
        page!.RequestedLimit.Should().Be(10);
        page.AppliedLimit.Should().Be(5);
        page.Items.Should().HaveCount(5);
    }
}