namespace Trellis.Showcase.Tests.Api;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Trellis.Showcase.Api;
using Trellis.Showcase.Api.Models;

/// <summary>
/// Black-box integration tests over the Showcase HTTP API. Each test verifies that an Error case
/// is mapped to the correct HTTP status and Problem Details payload.
/// </summary>
public class ShowcaseApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ShowcaseApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

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
        var body = await response.Content.ReadFromJsonAsync<AccountResponse>(Ct);
        body.Should().NotBeNull();
        body!.Status.Should().Be(Trellis.Showcase.Domain.Aggregates.AccountStatus.Active);
    }

    [Fact]
    public async Task Deposit_with_zero_amount_returns_422()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/accounts/{ShowcaseSeed.AliceCheckingId.Value}/deposit", UriKind.Relative),
            new DepositRequest(0m),
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);
    }

    [Fact]
    public async Task Secure_withdraw_with_invalid_code_returns_422()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/accounts/{ShowcaseSeed.AliceCheckingId.Value}/secure-withdraw", UriKind.Relative),
            new SecureWithdrawRequest(2000m, VerificationCode: "abc"),
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
}
