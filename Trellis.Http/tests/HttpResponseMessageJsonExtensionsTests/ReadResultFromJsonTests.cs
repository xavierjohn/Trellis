using Trellis.Testing;
namespace Trellis.Http.Tests.HttpResponseMessageJsonExtensionsTests;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Trellis;

public class ReadResultFromJsonTests
{
    readonly Error.NotFound _notFoundError = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Person not found" };

    private bool _callbackCalled;

    [Fact]
    public async Task Will_read_http_content_as_result()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson() { firstName = "Xavier", age = 50 }, SourceGenerationContext.Default.camelcasePerson)
        };

        // Act
        var result = await httpResponseMessage.ReadResultFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().firstName.Should().Be("Xavier");
        result.Unwrap().age.Should().Be(50);
    }

    [Fact]
    public async Task Will_return_failure_with_wrong_content()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = new StringContent("Bad JSON")
        };

        // Act
        var result = await httpResponseMessage.ReadResultFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.InternalServerError>();
        result.UnwrapError().Detail.Should().StartWith("Failed to deserialize HTTP response to camelcasePerson:");
    }

    [Fact]
    public async Task Will_not_throw_JsonException_with_wrong_content()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("Bad JSON")
        };

        // Act
        var result = await httpResponseMessage.ReadResultFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Detail.Should().Be("HTTP response is in a failed state for value camelcasePerson. Status code: BadGateway.");
    }

    [Fact]
    public async Task Will_return_failure_with_null_content()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = null
        };

        // Act
        var result = await httpResponseMessage.ReadResultFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.InternalServerError>();
        result.UnwrapError().Detail.Should().StartWith("Failed to deserialize HTTP response to camelcasePerson:");
    }

    [Fact]
    public async Task Successful_response_with_null_json_value_Returns_failure()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        };

        // Act
        var result = await httpResponseMessage.ReadResultFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.InternalServerError>();
        result.UnwrapError().Detail.Should().Be("HTTP response was null for value camelcasePerson.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Deserialize_is_case_sensitive(bool propertyNameCaseInsensitive)
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson() { firstName = "Xavier", age = 50 }, SourceGenerationContext.Default.camelcasePerson)
        };

        // Act
        var result = await httpResponseMessage.ReadResultFromJsonAsync(
            propertyNameCaseInsensitive ? SourceGenerationCaseInsenstiveContext.Default.PascalPerson : SourceGenerationContext.Default.PascalPerson,
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        if (propertyNameCaseInsensitive)
        {
            result.Unwrap().FirstName.Should().Be("Xavier");
            result.Unwrap().Age.Should().Be(50);
        }
        else
        {
            result.Unwrap().FirstName.Should().Be(string.Empty);
            result.Unwrap().Age.Should().Be(0);
        }
    }

    [Fact]
    public async Task When_HttpResponseMessage_is_Task_Will_read_http_content_as_result()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson() { firstName = "Xavier", age = 50 }, SourceGenerationContext.Default.camelcasePerson)
        };
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        var result = await taskHttpResponseMessage.ReadResultFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().firstName.Should().Be("Xavier");
        result.Unwrap().age.Should().Be(50);
    }

    [Fact]
    public async Task Will_callback_on_failure()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Expected space invaders.")
        };

        var callbackCalled = false;
        async Task<Error> CallbackFailedStatusCode(HttpResponseMessage response, string context, CancellationToken cancellationToken)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            content.Should().Be("Expected space invaders.");
            context.Should().Be("Hello");
            callbackCalled = true;
            return new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Bad request" };
        }

        // Act
        var result = await httpResponseMessage.HandleFailureAsync(CallbackFailedStatusCode, "Hello", CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().Be(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Bad request" });
        callbackCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Will_task_callback_on_failure()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Expected space invaders.")
        };
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        var callbackCalled = false;
        async Task<Error> Callback(HttpResponseMessage response, int context, CancellationToken cancellationToken)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            content.Should().Be("Expected space invaders.");
            context.Should().Be(5);
            callbackCalled = true;
            return new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Bad request" };
        }

        // Act
        var result = await taskHttpResponseMessage.HandleFailureAsync(Callback, 5, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().Be(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Bad request" });
        callbackCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Will_not_callback_on_success()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson() { firstName = "Chris", age = 18 }, SourceGenerationContext.Default.camelcasePerson)
        };

        // Act
        var result = await httpResponseMessage
            .HandleFailureAsync(CallbackFailedStatusCode, "Common", CancellationToken.None)
            .ReadResultFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _callbackCalled.Should().BeFalse();
        result.Unwrap().firstName.Should().Be("Chris");
        result.Unwrap().age.Should().Be(18);
    }

    [Fact]
    public async Task Will_task_not_callback_on_success()
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
            .ReadResultFromJsonAsync(SourceGenerationContext.Default.camelcasePerson, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _callbackCalled.Should().BeFalse();
        result.Unwrap().firstName.Should().Be("Chris");
        result.Unwrap().age.Should().Be(18);
    }

    private Task<Error> CallbackFailedStatusCode(HttpResponseMessage response, string context, CancellationToken cancellationToken)
    {
        _callbackCalled = true;
        context.Should().Be("Common");
        return Task.FromResult((Error)new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Bad request" });
    }

    #region CancellationToken Tests

    [Fact]
    public async Task Should_respect_cancellation_token_when_already_cancelled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson() { firstName = "Xavier", age = 50 }, SourceGenerationContext.Default.camelcasePerson)
        };

        // Act
        Func<Task> act = async () => await httpResponseMessage.ReadResultFromJsonAsync(
            SourceGenerationContext.Default.camelcasePerson,
            cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task HandleFailureAsync_should_respect_cancellation_token()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.BadRequest);

        async Task<Error> Callback(HttpResponseMessage response, string context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return new Error.BadRequest("bad.request") { Detail = "Error" };
        }

        // Act
        Func<Task> act = async () => await httpResponseMessage.HandleFailureAsync(
            Callback,
            "context",
            cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Various HTTP Status Codes

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Should_return_failure_for_various_error_status_codes(HttpStatusCode statusCode)
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(statusCode)
        {
            Content = new StringContent("Error content")
        };

        // Act
        var result = await httpResponseMessage.ReadResultFromJsonAsync(
            SourceGenerationContext.Default.camelcasePerson,
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Detail.Should().Contain(statusCode.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Accepted)]
    public async Task Should_handle_various_success_status_codes(HttpStatusCode statusCode)
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(statusCode)
        {
            Content = JsonContent.Create(new camelcasePerson() { firstName = "Xavier", age = 50 }, SourceGenerationContext.Default.camelcasePerson)
        };

        // Act
        var result = await httpResponseMessage.ReadResultFromJsonAsync(
            SourceGenerationContext.Default.camelcasePerson,
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().firstName.Should().Be("Xavier");
        result.Unwrap().age.Should().Be(50);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.ResetContent)]
    public async Task Should_treat_no_content_status_codes_as_missing_value(HttpStatusCode statusCode)
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(statusCode)
        {
            Content = JsonContent.Create(new camelcasePerson() { firstName = "Xavier", age = 50 }, SourceGenerationContext.Default.camelcasePerson)
        };

        // Act
        var result = await httpResponseMessage.ReadResultFromJsonAsync(
            SourceGenerationContext.Default.camelcasePerson,
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Detail.Should().Be("HTTP response was null for value camelcasePerson.");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task Should_handle_empty_json_content()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = new StringContent("", Encoding.UTF8, "application/json")
        };

        // Act
        var result = await httpResponseMessage.ReadResultFromJsonAsync(
            SourceGenerationContext.Default.camelcasePerson,
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.InternalServerError>();
        result.UnwrapError().Detail.Should().StartWith("Failed to deserialize HTTP response to camelcasePerson:");
    }

    [Fact]
    public async Task Should_handle_whitespace_only_content()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = new StringContent("   ", Encoding.UTF8, "application/json")
        };

        // Act
        var result = await httpResponseMessage.ReadResultFromJsonAsync(
            SourceGenerationContext.Default.camelcasePerson,
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().BeOfType<Error.InternalServerError>();
        result.UnwrapError().Detail.Should().StartWith("Failed to deserialize HTTP response to camelcasePerson:");
    }

    [Fact]
    public async Task Should_handle_large_json_payload()
    {
        // Arrange
        var largePerson = new camelcasePerson
        {
            firstName = new string('X', 10000),
            age = 50
        };

        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(largePerson, SourceGenerationContext.Default.camelcasePerson)
        };

        // Act
        var result = await httpResponseMessage.ReadResultFromJsonAsync(
            SourceGenerationContext.Default.camelcasePerson,
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().firstName.Should().HaveLength(10000);
        result.Unwrap().age.Should().Be(50);
    }

    [Fact]
    public async Task Should_handle_json_with_extra_properties()
    {
        // Arrange
        var jsonWithExtra = """{"firstName":"Xavier","age":50,"extraProperty":"ignored"}""";
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonWithExtra, Encoding.UTF8, "application/json")
        };

        // Act
        var result = await httpResponseMessage.ReadResultFromJsonAsync(
            SourceGenerationContext.Default.camelcasePerson,
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().firstName.Should().Be("Xavier");
        result.Unwrap().age.Should().Be(50);
    }

    [Fact]
    public async Task Should_handle_incorrect_content_type()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"firstName":"Xavier","age":50}""",
                Encoding.UTF8,
                "text/plain")
        };

        // Act
        var result = await httpResponseMessage.ReadResultFromJsonAsync(
            SourceGenerationContext.Default.camelcasePerson,
            CancellationToken.None);

        // Assert - Should still work despite wrong content type
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().firstName.Should().Be("Xavier");
    }

    #endregion

    #region Result-Wrapped Scenarios

    [Fact]
    public async Task Result_wrapped_HttpResponseMessage_with_success_should_deserialize()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson() { firstName = "Alice", age = 30 }, SourceGenerationContext.Default.camelcasePerson)
        };
        var resultResponse = Result.Ok(httpResponseMessage);

        // Act
        var result = await resultResponse.ReadResultFromJsonAsync(
            SourceGenerationContext.Default.camelcasePerson,
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().firstName.Should().Be("Alice");
        result.Unwrap().age.Should().Be(30);
    }

    [Fact]
    public async Task Result_wrapped_HttpResponseMessage_with_failure_should_propagate_error()
    {
        // Arrange
        var error = new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Initial validation failed" };
        var resultResponse = Result.Fail<HttpResponseMessage>(error);

        // Act
        var result = await resultResponse.ReadResultFromJsonAsync(
            SourceGenerationContext.Default.camelcasePerson,
            CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.UnwrapError().Should().Be(error);
    }

    [Fact]
    public async Task Task_Result_wrapped_HttpResponseMessage_should_work()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new camelcasePerson() { firstName = "Bob", age = 25 }, SourceGenerationContext.Default.camelcasePerson)
        };
        var taskResultResponse = Task.FromResult(Result.Ok(httpResponseMessage));

        // Act
        var result = await taskResultResponse.ReadResultFromJsonAsync(
            SourceGenerationContext.Default.camelcasePerson,
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().firstName.Should().Be("Bob");
        result.Unwrap().age.Should().Be(25);
    }

    #endregion
}
