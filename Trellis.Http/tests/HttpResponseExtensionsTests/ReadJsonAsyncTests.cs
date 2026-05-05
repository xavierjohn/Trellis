namespace Trellis.Http.Tests.HttpResponseExtensionsTests;

using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Testing;

public class ReadJsonAsyncTests
{
    [Fact]
    public async Task Already_failed_result_short_circuits_with_original_error()
    {
        var error = new Error.NotFound(new ResourceRef("User", "1"));
        var task = Task.FromResult(Result.Fail<HttpResponseMessage>(error));

        var result = await task.ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeFailureOfType<Error.NotFound>()
            .Which.Should().Be(error);
    }

    [Fact]
    public async Task Already_failed_result_with_null_jsonTypeInfo_does_not_throw()
    {
        var error = new Error.NotFound(new ResourceRef("User", "1"));
        var task = Task.FromResult(Result.Fail<HttpResponseMessage>(error));

        var result = await task.ReadJsonAsync<camelcasePerson>(jsonTypeInfo: null!, CancellationToken.None);

        result.Should().BeFailureOfType<Error.NotFound>();
    }

    [Fact]
    public async Task Success_with_valid_json_returns_deserialized_value_and_disposes_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson { firstName = "Xavier", age = 50 }, SourceGenerationContext.Default.camelcasePerson),
        };
        var task = Task.FromResult(Result.Ok<HttpResponseMessage>(tracker));

        var result = await task.ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        var person = result.Should().BeSuccess().Subject;
        person.firstName.Should().Be("Xavier");
        person.age.Should().Be(50);
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Success_with_invalid_json_returns_Fail_and_disposes_response_and_does_not_leak_JsonException()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Not JSON"),
        };
        var task = Task.FromResult(Result.Ok<HttpResponseMessage>(tracker));

        var result = await task.ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeFailureOfType<Error.InternalServerError>();
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Invalid_json_detail_uses_structured_position_not_response_body()
    {
        // Inspection finding M-H3 + GPT-5.5 pre-commit refinement: the Detail must use only
        // line / byte position info, not ex.Message (raw snippet) and not ex.Path (which can
        // contain user-controlled dictionary keys like $.customers['alice@example.com']).
        var bodyWithUserData = "{ \"firstName\": \"Xavier\", \"age\": \"not-a-number-12345\" }";
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(bodyWithUserData),
        };
        var task = Task.FromResult(Result.Ok<HttpResponseMessage>(tracker));

        var result = await task.ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        var err = result.Should().BeFailureOfType<Error.InternalServerError>().Subject;
        err.Detail.Should().NotContain("Xavier", "user data from the response body must not appear in the failure detail");
        err.Detail.Should().NotContain("not-a-number-12345", "user data from the response body must not appear in the failure detail");
        err.Detail.Should().NotContain("$.", "JsonException.Path can contain user-controlled dictionary keys; do not surface it");
        err.Detail.Should().Contain("camelcasePerson", "type name is acceptable diagnostic detail");
    }

    [Fact]
    public async Task Success_with_empty_body_returns_Fail_and_disposes_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Array.Empty<byte>()),
        };
        var task = Task.FromResult(Result.Ok<HttpResponseMessage>(tracker));

        var result = await task.ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeFailureOfType<Error.InternalServerError>();
        tracker.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.ResetContent)]
    public async Task Success_with_NoContent_or_ResetContent_returns_Fail_and_disposes_response(HttpStatusCode status)
    {
        var tracker = new TrackingHttpResponseMessage(status);
        var task = Task.FromResult(Result.Ok<HttpResponseMessage>(tracker));

        var result = await task.ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeFailureOfType<Error.InternalServerError>();
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Non_success_status_returns_Fail_with_status_in_detail_and_disposes_response()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.BadGateway);
        var task = Task.FromResult(Result.Ok<HttpResponseMessage>(tracker));

        var result = await task.ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        result.Should().BeFailureOfType<Error.InternalServerError>()
            .Which.Detail.Should().Contain("BadGateway");
        tracker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Null_jsonTypeInfo_on_Ok_throws_ArgumentNullException()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        var task = Task.FromResult(Result.Ok<HttpResponseMessage>(response));

        var act = async () => await task.ReadJsonAsync<camelcasePerson>(jsonTypeInfo: null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Null_jsonTypeInfo_on_Ok_disposes_response_before_throwing()
    {
        // Inspection finding M-H2: the response was awaited and held in `message`,
        // so the API must dispose it even when jsonTypeInfo is null. The previous
        // implementation null-checked jsonTypeInfo *after* await but *before* the
        // try/finally that disposes — leaking the response.
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK);
        var task = Task.FromResult(Result.Ok<HttpResponseMessage>(tracker));

        var act = async () => await task.ReadJsonAsync<camelcasePerson>(jsonTypeInfo: null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
        tracker.Disposed.Should().BeTrue("ReadJsonAsync's disposal contract must hold even on the null-jsonTypeInfo path");
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson { firstName = "X", age = 1 }, SourceGenerationContext.Default.camelcasePerson),
        };
        var task = Task.FromResult(Result.Ok<HttpResponseMessage>(tracker));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await task.ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Throws_ArgumentNullException_when_response_task_is_null()
    {
        Task<Result<HttpResponseMessage>> task = null!;

        var act = async () => await task.ReadJsonAsync(SourceGenerationContext.Default.camelcasePerson);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("response");
    }
}