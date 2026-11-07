namespace Trellis.Http.Tests.HttpResponseExtensionsTests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Trellis;
using Trellis.Testing;

/// <summary>
/// The strict default status map must produce exactly the error the API reference documents for each status:
/// the error kind, its code, its inner payload and the detail text. The kind alone is not the contract, because
/// a caller reads the code and the payload to decide what to do next.
/// </summary>
public class ToResultAsyncStatusPayloadTests
{
    private static readonly ResourceRef s_resource = ResourceRef.For("HttpResponse");

    private static readonly Dictionary<int, Error> s_documented = new()
    {
        [400] = Error.InvalidInput.ForRule(code: ValidationCodes.HttpBadRequest),
        [401] = new Error.AuthenticationRequired(),
        [403] = new Error.Forbidden(ValidationCodes.HttpForbidden),
        [404] = new Error.NotFound(s_resource),
        [406] = new Error.TransportFault(new HttpError.NotAcceptable(EquatableArray<string>.Empty)),
        [409] = new Error.Conflict(Resource: null, Code: ValidationCodes.HttpConflict),
        [410] = new Error.Gone(s_resource),
        [412] = new Error.TransportFault(new HttpError.PreconditionFailed(s_resource, PreconditionKind.IfMatch)),
        [413] = new Error.TransportFault(new HttpError.ContentTooLarge()),
        [415] = new Error.TransportFault(new HttpError.UnsupportedMediaType(EquatableArray<string>.Empty)),
        [422] = Error.InvalidInput.ForRule(code: ValidationCodes.HttpUnprocessableContent),
        [428] = new Error.TransportFault(new HttpError.PreconditionRequired(PreconditionKind.IfMatch)),
        [429] = new Error.RateLimited(),
        [501] = new Error.Unexpected(Code: FaultCodes.NotImplemented),
        [503] = new Error.Unavailable(),
    };

    public static TheoryData<int> DocumentedStatuses =>
        new() { 400, 401, 403, 404, 406, 409, 410, 412, 413, 415, 422, 428, 429, 501, 503 };

    public static TheoryData<int> UnmappedStatuses =>
        new() { 300, 302, 418, 500, 502, 504, 599 };

    private static string DetailFor(int status) =>
        $"HTTP response returned status code {status} ({(HttpStatusCode)status}).";

    private static async Task<(Result<HttpResponseMessage> Result, TrackingHttpResponseMessage Tracker)> Map(
        int status,
        Action<TrackingHttpResponseMessage>? configure = null)
    {
        var tracker = new TrackingHttpResponseMessage((HttpStatusCode)status);
        configure?.Invoke(tracker);
        var result = await Task.FromResult<HttpResponseMessage>(tracker).ToResultAsync();
        return (result, tracker);
    }

    [Theory]
    [MemberData(nameof(DocumentedStatuses))]
    public async Task A_documented_status_maps_to_exactly_its_documented_error(int status)
    {
        var (result, tracker) = await Map(status);

        result.Should().BeFailure().Which.Should().Be(s_documented[status] with { Detail = DetailFor(status) });
        tracker.Disposed.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(UnmappedStatuses))]
    public async Task An_unmapped_status_maps_to_the_stable_response_fault_with_a_fresh_fault_id(int status)
    {
        var (result, tracker) = await Map(status);

        var error = result.Should().HaveErrorCode(FaultCodes.HttpResponseFault)
            .And.BeFailureOfType<Error.Unexpected>().Subject;
        error.Detail.Should().Be(DetailFor(status));
        error.FaultId.Should().NotBeNullOrWhiteSpace();
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task A_405_without_Allow_and_a_416_without_a_content_range_length_fall_back_to_the_stable_response_fault()
    {
        var (without405, _) = await Map(405);
        var (without416, _) = await Map(416);
        var (noLength416, _) = await Map(416, tracker =>
        {
            tracker.Content = new ByteArrayContent([]);
            tracker.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 99);
        });

        foreach (var (status, result) in new[] { (405, without405), (416, without416), (416, noLength416) })
        {
            var error = result.Should().HaveErrorCode(FaultCodes.HttpResponseFault)
                .And.BeFailureOfType<Error.Unexpected>().Subject;
            error.Detail.Should().Be(DetailFor(status));
            error.FaultId.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Theory]
    [InlineData(429, null, null, null)]
    [InlineData(429, "60", 60, null)]
    [InlineData(429, "0", 0, null)]
    [InlineData(429, "Fri, 01 Jan 2027 00:00:00 GMT", null, "2027-01-01T00:00:00+00:00")]
    [InlineData(503, null, null, null)]
    [InlineData(503, "60", 60, null)]
    [InlineData(503, "0", 0, null)]
    [InlineData(503, "Fri, 01 Jan 2027 00:00:00 GMT", null, "2027-01-01T00:00:00+00:00")]
    public async Task Retry_After_is_carried_into_the_retry_advice_when_present(int status, string? header, int? seconds, string? at)
    {
        var (result, _) = await Map(status, tracker =>
        {
            if (header is not null)
                tracker.Headers.TryAddWithoutValidation("Retry-After", header);
        });

        var expected = seconds is null && at is null
            ? (RetryAdvice?)null
            : new RetryAdvice(
                After: seconds is { } s ? TimeSpan.FromSeconds(s) : null,
                At: at is null ? null : DateTimeOffset.Parse(at, CultureInfo.InvariantCulture));
        var actual = status == 429
            ? result.Should().BeFailureOfType<Error.RateLimited>().Subject.Retry
            : result.Should().BeFailureOfType<Error.Unavailable>().Subject.Retry;
        actual.Should().Be(expected);
    }
}
