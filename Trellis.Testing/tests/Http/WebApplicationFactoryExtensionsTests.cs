namespace Trellis.Testing.Tests.Http;

using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Tests for <see cref="WebApplicationFactoryExtensions.CreateClientWithActor{TEntryPoint}"/>.
/// </summary>
public sealed class WebApplicationFactoryExtensionsTests : IDisposable
{
    private readonly TestFactory _factory = new();

    [Fact]
    public void CreateClientWithActor_SetsXTestActorHeader_WithActorIdAndPermissions()
    {
        var client = _factory.CreateClientWithActor("user-1", "Orders.Read", "Orders.Write");

        client.DefaultRequestHeaders.TryGetValues("X-Test-Actor", out var values).Should().BeTrue();
        var json = values!.Single();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("Id").GetString().Should().Be("user-1");
        doc.RootElement.GetProperty("Permissions").EnumerateArray()
            .Select(e => e.GetString())
            .Should().Equal("Orders.Read", "Orders.Write");
        doc.RootElement.GetProperty("ForbiddenPermissions").EnumerateArray().Should().BeEmpty();
        doc.RootElement.GetProperty("Attributes").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public void CreateClientWithActor_EmptyPermissions_SetsHeaderWithEmptyArray()
    {
        var client = _factory.CreateClientWithActor("admin");

        client.DefaultRequestHeaders.TryGetValues("X-Test-Actor", out var values).Should().BeTrue();
        var json = values!.Single();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("Id").GetString().Should().Be("admin");
        doc.RootElement.GetProperty("Permissions").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public void CreateClientWithActor_NullPermissions_SetsHeaderWithEmptyArray()
    {
        var client = _factory.CreateClientWithActor("admin", null!);

        client.DefaultRequestHeaders.TryGetValues("X-Test-Actor", out var values).Should().BeTrue();
        var json = values!.Single();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("Id").GetString().Should().Be("admin");
        doc.RootElement.GetProperty("Permissions").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public void CreateClientWithActor_SinglePermission_SetsHeaderCorrectly()
    {
        var client = _factory.CreateClientWithActor("user-2", "Documents.Publish");

        client.DefaultRequestHeaders.TryGetValues("X-Test-Actor", out var values).Should().BeTrue();
        var json = values!.Single();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("Id").GetString().Should().Be("user-2");
        doc.RootElement.GetProperty("Permissions").EnumerateArray()
            .Select(e => e.GetString())
            .Should().Equal(["Documents.Publish"]);
    }

    [Fact]
    public void CreateClientWithActor_ReturnsUsableHttpClient()
    {
        var client = _factory.CreateClientWithActor("user-1", "Orders.Read");

        client.Should().NotBeNull();
        client.BaseAddress.Should().NotBeNull();
    }

    public void Dispose() => _factory.Dispose();

    /// <summary>
    /// Minimal <see cref="WebApplicationFactory{TEntryPoint}"/> that creates an empty test server.
    /// </summary>
    private sealed class TestFactory : WebApplicationFactory<TestFactory>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = new HostBuilder()
                .ConfigureWebHost(wb =>
                {
                    wb.UseTestServer();
                    wb.Configure(_ => { });
                })
                .Build();
            host.Start();
            return host;
        }
    }
}