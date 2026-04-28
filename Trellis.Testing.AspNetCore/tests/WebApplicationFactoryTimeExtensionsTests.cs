namespace Trellis.Testing.AspNetCore.Tests;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

/// <summary>
/// Tests for <see cref="WebApplicationFactoryTimeExtensions"/>.
/// Verifies that the out-overloads return a <see cref="FakeTimeProvider"/> with a
/// deterministic starting instant by default, that an explicit instant can be supplied,
/// and that null arguments are rejected.
/// </summary>
public sealed class WebApplicationFactoryTimeExtensionsTests : IDisposable
{
    private readonly TestFactory _factory = new();

    [Fact]
    public void DefaultTestStartInstant_IsFixedDeterministicValue() =>
        // ga-17: contract assertion. If this changes, every consumer of the parameterless
        // out-overload will see a different baseline — bump documentation accordingly.
        WebApplicationFactoryTimeExtensions.DefaultTestStartInstant
            .Should().Be(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void WithFakeTimeProvider_ParameterlessOut_UsesDeterministicDefaultInstant()
    {
        // The defining ga-17 guarantee: tests using the parameterless out-overload get a
        // FakeTimeProvider seeded at the deterministic DefaultTestStartInstant — never the
        // wall clock — so timestamp assertions are stable across runs.
        _ = _factory.WithFakeTimeProvider(out var fake);

        fake.GetUtcNow().Should().Be(WebApplicationFactoryTimeExtensions.DefaultTestStartInstant);
    }

    [Fact]
    public void WithFakeTimeProvider_ExplicitInstantOut_UsesSuppliedInstant()
    {
        var requested = new DateTimeOffset(2030, 6, 15, 12, 30, 0, TimeSpan.Zero);

        _ = _factory.WithFakeTimeProvider(requested, out var fake);

        fake.GetUtcNow().Should().Be(requested);
    }

    [Fact]
    public void WithFakeTimeProvider_ReturnsConfiguredFactoryInstance()
    {
        // Ensure the chain returns a usable WebApplicationFactory rather than throwing or
        // returning null. Side-effects on the WebHost services container fire lazily inside
        // the test pipeline and are exercised by full integration suites in real apps.
        var configured = _factory.WithFakeTimeProvider(out _);

        configured.Should().NotBeNull();
    }

    [Fact]
    public void WithFakeTimeProvider_NullFactory_Throws()
    {
        WebApplicationFactory<TestFactory> nullFactory = null!;
        var fake = new FakeTimeProvider();

        Action act = () => nullFactory.WithFakeTimeProvider(fake);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithFakeTimeProvider_NullProvider_Throws()
    {
        FakeTimeProvider nullProvider = null!;

        Action act = () => _factory.WithFakeTimeProvider(nullProvider);

        act.Should().Throw<ArgumentNullException>();
    }

    public void Dispose() => _factory.Dispose();

    /// <summary>
    /// Minimal <see cref="WebApplicationFactory{TEntryPoint}"/> that creates an empty test server.
    /// Mirrors the pattern used by <c>WebApplicationFactoryExtensionsTests</c>.
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