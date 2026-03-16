namespace Trellis.Http.Tests.HttpResponseMessageJsonExtensionsTests;

using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Trellis;

public class ReadResultMaybeFromJsonTests
{
    readonly NotFoundError _notFoundError = Error.NotFound("Person not found");

    private bool _callbackCalled;

    [Fact]
    public async Task Successful_response_with_valid_json_Returns_deserialized_content()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson() { firstName = "Xavier", age = 50 }, SourceGenerationContext.Default.camelcasePerson)
        };

        // Act
        var result = await httpResponseMessage.ReadResultMaybeFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var maybePerson = result.Value;
        maybePerson.HasValue.Should().BeTrue();
        maybePerson.Value.firstName.Should().Be("Xavier");
        maybePerson.Value.age.Should().Be(50);
    }

    [Fact]
    public async Task Successful_response_with_invalid_json_Throws_JsonException()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = new StringContent("Bad JSON")
        };

        // Act
        Func<Task> act = async () => await httpResponseMessage.ReadResultMaybeFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task Failed_response_status_code_Returns_failure_with_error_message()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("Bad JSON")
        };

        // Act
        var result = await httpResponseMessage.ReadResultMaybeFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Detail.Should().Be("HTTP response is in a failed state for value camelcasePerson. Status code: BadGateway.");
    }

    [Fact]
    public async Task Successful_response_with_null_content_Throws_JsonException()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = null
        };

        // Act
        Func<Task> act = async () => await httpResponseMessage.ReadResultMaybeFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task Null_response_Throws_ArgumentNullException()
    {
        HttpResponseMessage httpResponseMessage = null!;

        Func<Task> act = async () => await httpResponseMessage.ReadResultMaybeFromJsonAsync(
            SourceGenerationContext.Default.camelcasePerson,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .Where(exception => exception.ParamName == "response");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Deserialization_Respects_case_sensitivity_options(bool propertyNameCaseInsensitive)
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson() { firstName = "Xavier", age = 50 }, SourceGenerationContext.Default.camelcasePerson)
        };

        // Act
        Result<Maybe<PascalPerson>> result = await httpResponseMessage.ReadResultMaybeFromJsonAsync(
            propertyNameCaseInsensitive ? SourceGenerationCaseInsenstiveContext.Default.PascalPerson : SourceGenerationContext.Default.PascalPerson,
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var maybePerson = result.Value;
        maybePerson.HasValue.Should().BeTrue();
        if (propertyNameCaseInsensitive)
        {
            maybePerson.Value.FirstName.Should().Be("Xavier");
            maybePerson.Value.Age.Should().Be(50);
        }
        else
        {
            maybePerson.Value.FirstName.Should().Be(string.Empty);
            maybePerson.Value.Age.Should().Be(0);
        }
    }

    [Fact]
    public async Task Task_wrapped_successful_response_Returns_deserialized_content()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson() { firstName = "Xavier", age = 50 }, SourceGenerationContext.Default.camelcasePerson)
        };
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        Result<Maybe<camelcasePerson>> result = await taskHttpResponseMessage.ReadResultMaybeFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var maybePerson = result.Value;
        maybePerson.Value.firstName.Should().Be("Xavier");
        maybePerson.Value.age.Should().Be(50);
    }

    [Fact]
    public async Task Successful_response_with_null_json_value_Returns_none()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        };

        // Act
        Result<Maybe<camelcasePerson>> result = await httpResponseMessage.ReadResultMaybeFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        Maybe<camelcasePerson> maybePerson = result.Value;
        maybePerson.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task NoContent_response_without_body_Returns_none()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.NoContent)
        {
            Content = null
        };

        // Act
        Result<Maybe<camelcasePerson>> result = await httpResponseMessage.ReadResultMaybeFromJsonAsync(
            SourceGenerationContext.Default.camelcasePerson,
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public async Task ResetContent_response_without_body_Returns_none()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.ResetContent)
        {
            Content = null
        };

        // Act
        Result<Maybe<camelcasePerson>> result = await httpResponseMessage.ReadResultMaybeFromJsonAsync(
            SourceGenerationContext.Default.camelcasePerson,
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.HasNoValue.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.ResetContent)]
    public async Task NoContent_status_with_empty_content_and_no_length_header_Returns_none(HttpStatusCode statusCode)
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(statusCode)
        {
            Content = new EmptyUnknownLengthContent()
        };

        // Act
        Result<Maybe<camelcasePerson>> result = await httpResponseMessage.ReadResultMaybeFromJsonAsync(
            SourceGenerationContext.Default.camelcasePerson,
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.HasNoValue.Should().BeTrue();
    }

    [Fact]
    public async Task Successful_response_with_failure_handler_Does_not_invoke_callback()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson() { firstName = "Chris", age = 18 }, SourceGenerationContext.Default.camelcasePerson)
        };

        // Act
        var result = await httpResponseMessage
            .HandleFailureAsync(CallbackFailedStatusCode, "Common", CancellationToken.None)
            .ReadResultMaybeFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _callbackCalled.Should().BeFalse();
        var maybePerson = result.Value;
        maybePerson.HasValue.Should().BeTrue();
        maybePerson.Value.firstName.Should().Be("Chris");
        maybePerson.Value.age.Should().Be(18);
    }

    private sealed class EmptyUnknownLengthContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    [Fact]
    public async Task Task_wrapped_successful_response_with_failure_handler_Does_not_invoke_callback()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson() { firstName = "Chris", age = 18 }, SourceGenerationContext.Default.camelcasePerson)
        };
        var httpResponseMessageTask = Task.FromResult(httpResponseMessage);

        // Act
        var result = await httpResponseMessageTask
            .HandleFailureAsync(CallbackFailedStatusCode, "Common", CancellationToken.None)
            .ReadResultMaybeFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _callbackCalled.Should().BeFalse();
        var maybePerson = result.Value;
        maybePerson.HasValue.Should().BeTrue();
        maybePerson.Value.firstName.Should().Be("Chris");
        maybePerson.Value.age.Should().Be(18);
    }

    [Fact]
    public async Task Failed_response_with_failure_handler_Invokes_callback_and_returns_failure()
    {
        // Arrange
        _callbackCalled = false;
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Bad request content")
        };

        // Act
        var result = await httpResponseMessage
            .HandleFailureAsync(CallbackFailedStatusCode, "Common", CancellationToken.None)
            .ReadResultMaybeFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        _callbackCalled.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>();
        result.Error.Detail.Should().Be("Bad request");
    }

    [Fact]
    public async Task Task_wrapped_failed_response_with_failure_handler_Invokes_callback_and_returns_failure()
    {
        // Arrange
        _callbackCalled = false;
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Bad request content")
        };
        var httpResponseMessageTask = Task.FromResult(httpResponseMessage);

        // Act
        var result = await httpResponseMessageTask
            .HandleFailureAsync(CallbackFailedStatusCode, "Common", CancellationToken.None)
            .ReadResultMaybeFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        _callbackCalled.Should().BeTrue();
        result.Error.Should().BeOfType<NotFoundError>();
        result.Error.Detail.Should().Be("Bad request");
    }

    [Fact]
    public async Task Result_wrapped_successful_response_Returns_deserialized_content()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson() { firstName = "Anna", age = 25 }, SourceGenerationContext.Default.camelcasePerson)
        };
        var resultHttpResponseMessage = Result.Success(httpResponseMessage);

        // Act
        var result = await resultHttpResponseMessage.ReadResultMaybeFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var maybePerson = result.Value;
        maybePerson.HasValue.Should().BeTrue();
        maybePerson.Value.firstName.Should().Be("Anna");
        maybePerson.Value.age.Should().Be(25);
    }

    [Fact]
    public async Task Result_wrapped_failure_Propagates_error()
    {
        // Arrange
        var error = Error.Validation("Validation failed");
        var resultHttpResponseMessage = Result.Failure<HttpResponseMessage>(error);

        // Act
        var result = await resultHttpResponseMessage.ReadResultMaybeFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public async Task Task_result_wrapped_successful_response_Returns_deserialized_content()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson() { firstName = "Bob", age = 30 }, SourceGenerationContext.Default.camelcasePerson)
        };
        var taskResultHttpResponseMessage = Task.FromResult(Result.Success(httpResponseMessage));

        // Act
        var result = await taskResultHttpResponseMessage.ReadResultMaybeFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var maybePerson = result.Value;
        maybePerson.HasValue.Should().BeTrue();
        maybePerson.Value.firstName.Should().Be("Bob");
        maybePerson.Value.age.Should().Be(30);
    }

    [Fact]
    public async Task Task_result_wrapped_failure_Propagates_error()
    {
        // Arrange
        var error = Error.Unauthorized("Unauthorized access");
        var taskResultHttpResponseMessage = Task.FromResult(Result.Failure<HttpResponseMessage>(error));

        // Act
        var result = await taskResultHttpResponseMessage.ReadResultMaybeFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    private Task<Error> CallbackFailedStatusCode(HttpResponseMessage response, string context, CancellationToken cancellationToken)
    {
        _callbackCalled = true;
        context.Should().Be("Common");
        return Task.FromResult((Error)Error.NotFound("Bad request"));
    }
}

#pragma warning disable IDE1006 // Naming Styles
public class camelcasePerson
#pragma warning restore IDE1006 // Naming Styles
{
    public string firstName { get; set; } = string.Empty;
    public int age { get; set; }
}

public class PascalPerson
{
    public string FirstName { get; set; } = string.Empty;
    public int Age { get; set; }
}

[JsonSerializable(typeof(camelcasePerson))]
[JsonSerializable(typeof(PascalPerson))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(camelcasePerson))]
[JsonSerializable(typeof(PascalPerson))]
internal partial class SourceGenerationCaseInsenstiveContext : JsonSerializerContext
{
}