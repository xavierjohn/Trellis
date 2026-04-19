using Trellis.Testing;
namespace Trellis.Http.Tests.HttpResponseMessageJsonExtensionsTests;

using System.Net;
using System.Threading.Tasks;
using Trellis;

/// <summary>
/// Tests for HandleNotFound and HandleNotFoundAsync extension methods.
/// These methods are independent of JSON deserialization and work with any HttpResponseMessage.
/// </summary>
public class HandleNotFoundTests
{
    readonly Error.NotFound _notFoundError = new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "Person not found" };

    #region Synchronous HandleNotFound Tests

    [Fact]
    public void HandleNotFound_with_404_status_should_return_failure()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.NotFound);

        // Act
        var result = httpResponseMessage.HandleNotFound(_notFoundError);

        // Assert
        result.Should().BeFailureOfType<Error.NotFound>()
            .Which.Should().HaveDetail("Person not found");
    }

    [Fact]
    public void HandleNotFound_with_successful_response_should_return_success()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK);

        // Act
        var result = httpResponseMessage.HandleNotFound(_notFoundError);

        // Assert
        result.Should().BeSuccess()
            .Which.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void HandleNotFound_with_non_404_errors_should_pass_through(HttpStatusCode statusCode)
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(statusCode);

        // Act
        var result = httpResponseMessage.HandleNotFound(_notFoundError);

        // Assert
        result.Should().BeSuccess()
            .Which.StatusCode.Should().Be(statusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public void HandleNotFound_with_various_success_codes_should_pass_through(HttpStatusCode statusCode)
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(statusCode);

        // Act
        var result = httpResponseMessage.HandleNotFound(_notFoundError);

        // Assert
        result.Should().BeSuccess()
            .Which.StatusCode.Should().Be(statusCode);
    }

    #endregion

    #region Asynchronous HandleNotFoundAsync Tests

    [Fact]
    public async Task HandleNotFoundAsync_with_404_status_should_return_failure()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.NotFound);
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        var result = await taskHttpResponseMessage.HandleNotFoundAsync(_notFoundError);

        // Assert
        result.Should().BeFailureOfType<Error.NotFound>()
            .Which.Should().HaveDetail("Person not found");
    }

    [Fact]
    public async Task HandleNotFoundAsync_with_successful_response_should_return_success()
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(HttpStatusCode.OK)
        {
            Content = new StringContent("Success")
        };
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        var result = await taskHttpResponseMessage.HandleNotFoundAsync(_notFoundError);

        // Assert
        result.Should().BeSuccess()
            .Which.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task HandleNotFoundAsync_with_non_404_errors_should_pass_through(HttpStatusCode statusCode)
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(statusCode);
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        var result = await taskHttpResponseMessage.HandleNotFoundAsync(_notFoundError);

        // Assert
        result.Should().BeSuccess()
            .Which.StatusCode.Should().Be(statusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task HandleNotFoundAsync_with_various_success_codes_should_pass_through(HttpStatusCode statusCode)
    {
        // Arrange
        using HttpResponseMessage httpResponseMessage = new(statusCode);
        var taskHttpResponseMessage = Task.FromResult(httpResponseMessage);

        // Act
        var result = await taskHttpResponseMessage.HandleNotFoundAsync(_notFoundError);

        // Assert
        result.Should().BeSuccess()
            .Which.StatusCode.Should().Be(statusCode);
    }

    #endregion
}
