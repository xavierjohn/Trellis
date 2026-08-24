namespace Trellis.Http.Tests.HttpResponseExtensionsTests;

using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Testing;

/// <summary>
/// Every <see cref="Error.Unexpected"/> the HTTP adapter produces must carry a stable, aggregatable
/// <see cref="Error.Code"/> and put the per-incident identifier in <c>FaultId</c>. These previously
/// passed a fresh GUID as the <c>Code</c> positional argument, which gave the wire an unbounded set
/// of code values and left <c>FaultId</c> null.
/// </summary>
public class StableFaultCodeTests
{
    private static Task<Result<HttpResponseMessage>> Respond(HttpResponseMessage message) =>
        Task.FromResult(Result.Ok(message));

    [Fact]
    public async Task ReadJsonAsync_on_failed_status_reports_response_not_success()
    {
        var result = await Respond(new TrackingHttpResponseMessage(HttpStatusCode.InternalServerError))
            .ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        var error = result.Should().BeFailureOfType<Error.Unexpected>().Subject;
        error.Code.Should().Be(FaultCodes.HttpResponseNotSuccess);
        error.FaultId.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.ResetContent)]
    public async Task ReadJsonAsync_on_bodyless_status_reports_response_no_body(HttpStatusCode status)
    {
        var result = await Respond(new TrackingHttpResponseMessage(status))
            .ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        var error = result.Should().BeFailureOfType<Error.Unexpected>().Subject;
        error.Code.Should().Be(FaultCodes.HttpResponseNoBody);
        error.FaultId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ReadJsonAsync_on_empty_body_reports_response_no_body()
    {
        var message = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Array.Empty<byte>()),
        };

        var result = await Respond(message)
            .ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeFailureOfType<Error.Unexpected>()
            .Which.Code.Should().Be(FaultCodes.HttpResponseNoBody);
    }

    [Fact]
    public async Task ReadJsonAsync_on_invalid_json_reports_response_invalid_body()
    {
        var message = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Not JSON"),
        };

        var result = await Respond(message)
            .ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        var error = result.Should().BeFailureOfType<Error.Unexpected>().Subject;
        error.Code.Should().Be(FaultCodes.HttpResponseInvalidBody);
        error.FaultId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ReadJsonAsync_on_json_null_reports_response_invalid_body()
    {
        var message = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json"),
        };

        var result = await Respond(message)
            .ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeFailureOfType<Error.Unexpected>()
            .Which.Code.Should().Be(FaultCodes.HttpResponseInvalidBody);
    }

    [Fact]
    public async Task ReadJsonMaybeAsync_on_failed_status_reports_response_not_success()
    {
        var result = await Respond(new TrackingHttpResponseMessage(HttpStatusCode.InternalServerError))
            .ReadJsonMaybeAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeFailureOfType<Error.Unexpected>()
            .Which.Code.Should().Be(FaultCodes.HttpResponseNotSuccess);
    }

    [Fact]
    public async Task ReadJsonMaybeAsync_on_invalid_json_reports_response_invalid_body()
    {
        var message = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Not JSON"),
        };

        var result = await Respond(message)
            .ReadJsonMaybeAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeFailureOfType<Error.Unexpected>()
            .Which.Code.Should().Be(FaultCodes.HttpResponseInvalidBody);
    }

    [Fact]
    public async Task ToResultAsync_on_unmapped_status_reports_response_fault()
    {
        var result = await Task.FromResult<HttpResponseMessage>(new TrackingHttpResponseMessage(HttpStatusCode.BadGateway))
            .ToResultAsync();

        var error = result.Should().BeFailureOfType<Error.Unexpected>().Subject;
        error.Code.Should().Be(FaultCodes.HttpResponseFault);
        error.FaultId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Codes_are_stable_across_calls_so_telemetry_can_aggregate()
    {
        static Task<Result<HttpResponseMessage>> Invalid() =>
            Task.FromResult(Result.Ok<HttpResponseMessage>(new TrackingHttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Not JSON"),
            }));

        var first = await Invalid().ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);
        var second = await Invalid().ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        var a = first.Should().BeFailureOfType<Error.Unexpected>().Subject;
        var b = second.Should().BeFailureOfType<Error.Unexpected>().Subject;

        a.Code.Should().Be(b.Code, "a dashboard must be able to group these failures");
        a.FaultId.Should().NotBe(b.FaultId, "each incident still needs its own identifier");
    }
}
