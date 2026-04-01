namespace Trellis.Asp.Tests;

using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

public class HttpPartialContentResultTest
{
    public static TheoryData<object> ValuesData =>
        [
            null!,
            "Test string",
            new Person
            {
                Id = 274,
                Name = "George",
            }
        ];

    [Theory]
    [MemberData(nameof(ValuesData))]
    public void InitializesStatusCodeAndValue(object value)
    {
        // Arrange & Act
        var contentRangeHeaderValue = new ContentRangeHeaderValue(1, 3, 10);
        var result = new PartialContentResult(contentRangeHeaderValue, value);

        // Assert
        result.StatusCode.Should().Be(StatusCodes.Status206PartialContent);
        result.Value.Should().Be(value);
    }

    [Theory]
    [InlineData(0, 3, 10, "items 0-3/10")]
    [InlineData(2, 5, null, "items 2-5/*")]
    public async Task SetContentRangeHeader(int rangeStart, int rangeEnd, int? totalLength, string expected)
    {
        // Arrange
        var result = new PartialContentResult(rangeStart, rangeEnd, totalLength, "Hello There");

        var httpContext = new DefaultHttpContext
        {
            RequestServices = CreateServices(),
        };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        // Act
        await result.ExecuteResultAsync(actionContext);

        // Assert
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status206PartialContent);
        httpContext.Response.Headers["Content-Range"].ToString().Should().Be(expected);
    }

    private static Microsoft.Extensions.DependencyInjection.ServiceProvider CreateServices()
    {
        var options = Options.Create(new MvcOptions());
        options.Value.OutputFormatters.Add(new StringOutputFormatter());
        options.Value.OutputFormatters.Add(new SystemTextJsonOutputFormatter(new JsonOptions().JsonSerializerOptions));

        var services = new ServiceCollection();
        services.AddSingleton<IActionResultExecutor<ObjectResult>>(new ObjectResultExecutor(
            new DefaultOutputFormatterSelector(options, NullLoggerFactory.Instance),
            new TestHttpResponseStreamWriterFactory(),
            NullLoggerFactory.Instance,
            options));

        return services.BuildServiceProvider();
    }

    private class Person
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}