namespace Trellis.Testing.AspNetCore;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

/// <summary>
/// Extension methods for <see cref="WebApplicationFactory{TEntryPoint}"/>
/// that simplify controlling time in integration tests.
/// </summary>
/// <remarks>
/// <para>
/// The factory-out overloads default to the well-known fixed instant
/// <see cref="DefaultTestStartInstant"/> (<c>2024-01-01T00:00:00Z</c>) so tests
/// are deterministic by default — repeated runs use the same starting clock and
/// any test that asserts on absolute timestamps does not flake based on wall time.
/// Pass an explicit <see cref="DateTimeOffset"/> when you need a different baseline.
/// </para>
/// </remarks>
public static class WebApplicationFactoryTimeExtensions
{
    /// <summary>
    /// The deterministic default starting instant used by the
    /// <see cref="WithFakeTimeProvider{TEntryPoint}(WebApplicationFactory{TEntryPoint}, out FakeTimeProvider)"/>
    /// overload — <c>2024-01-01T00:00:00Z</c>.
    /// </summary>
    /// <remarks>
    /// Chosen as a recent, round-numbered UTC instant to keep test diagnostics readable
    /// while remaining stable across runs. Tests that need a different baseline should
    /// use the overload that accepts an explicit <see cref="DateTimeOffset"/>.
    /// </remarks>
    public static readonly DateTimeOffset DefaultTestStartInstant =
        new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Returns a new <see cref="WebApplicationFactory{TEntryPoint}"/> that replaces the
    /// <see cref="TimeProvider"/> singleton with a <see cref="FakeTimeProvider"/>.
    /// Tests can rewind/advance time via <paramref name="fakeTimeProvider"/> to control
    /// timestamps set by domain logic and <c>EntityTimestampInterceptor</c>.
    /// </summary>
    /// <typeparam name="TEntryPoint">The entry point class of the web application under test.</typeparam>
    /// <param name="factory">The web application factory.</param>
    /// <param name="fakeTimeProvider">The shared <see cref="FakeTimeProvider"/> instance that tests use to control time.</param>
    /// <returns>A new factory with the fake time provider registered.</returns>
    /// <remarks>
    /// <para>The <see cref="FakeTimeProvider"/> is registered as a singleton, so all scopes
    /// (including per-request scopes in the HTTP pipeline) share the same clock.</para>
    /// <para>For full time control including EF Core interceptors, also wire
    /// <c>AddTrellisInterceptors(fakeTimeProvider)</c> via
    /// <see cref="ServiceCollectionDbProviderExtensions.ReplaceDbProvider{TContext}"/>.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Seed the fake clock 8 days before the "now" the test cares about so the test
    /// // only ever advances time forward — FakeTimeProvider.Advance rejects negative spans.
    /// var fakeTime = new FakeTimeProvider(WebApplicationFactoryTimeExtensions.DefaultTestStartInstant);
    /// _factory = _factory.WithFakeTimeProvider(fakeTime);
    ///
    /// // The order is submitted at "T0" (the deterministic baseline).
    /// await client.PostAsync("/api/orders/1/submission", null, ct);
    ///
    /// // Advance 8 days; the overdue endpoint now sees the order as past-due.
    /// fakeTime.Advance(TimeSpan.FromDays(8));
    /// var response = await client.GetAsync("/api/orders/overdue", ct);
    /// </code>
    /// </example>
    public static WebApplicationFactory<TEntryPoint> WithFakeTimeProvider<TEntryPoint>(
        this WebApplicationFactory<TEntryPoint> factory,
        FakeTimeProvider fakeTimeProvider)
        where TEntryPoint : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(fakeTimeProvider);

        return factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.ReplaceSingleton<TimeProvider>(fakeTimeProvider)));
    }

    /// <summary>
    /// Returns a new <see cref="WebApplicationFactory{TEntryPoint}"/> with a <see cref="FakeTimeProvider"/>
    /// registered, and outputs the provider for test use. The provider is initialized to the
    /// deterministic <see cref="DefaultTestStartInstant"/>.
    /// </summary>
    /// <typeparam name="TEntryPoint">The entry point class of the web application under test.</typeparam>
    /// <param name="factory">The web application factory.</param>
    /// <param name="fakeTimeProvider">When this method returns, contains the <see cref="FakeTimeProvider"/> instance.</param>
    /// <returns>A new factory with the fake time provider registered.</returns>
    /// <remarks>
    /// Defaults to <see cref="DefaultTestStartInstant"/> — <c>2024-01-01T00:00:00Z</c> — to keep
    /// integration tests deterministic across runs. If your test needs a specific baseline,
    /// use the overload that accepts an explicit <see cref="DateTimeOffset"/>.
    /// </remarks>
    public static WebApplicationFactory<TEntryPoint> WithFakeTimeProvider<TEntryPoint>(
        this WebApplicationFactory<TEntryPoint> factory,
        out FakeTimeProvider fakeTimeProvider)
        where TEntryPoint : class
        => factory.WithFakeTimeProvider(DefaultTestStartInstant, out fakeTimeProvider);

    /// <summary>
    /// Returns a new <see cref="WebApplicationFactory{TEntryPoint}"/> with a <see cref="FakeTimeProvider"/>
    /// registered at the specified <paramref name="startInstant"/>, and outputs the provider for test use.
    /// </summary>
    /// <typeparam name="TEntryPoint">The entry point class of the web application under test.</typeparam>
    /// <param name="factory">The web application factory.</param>
    /// <param name="startInstant">The instant the fake clock should report at start.</param>
    /// <param name="fakeTimeProvider">When this method returns, contains the <see cref="FakeTimeProvider"/> instance.</param>
    /// <returns>A new factory with the fake time provider registered.</returns>
    /// <remarks>
    /// Use this overload when a test needs a specific baseline — for example, a fixed-date
    /// fixture or a value that maps to a known database seed. Otherwise prefer the
    /// parameterless out-overload, which uses <see cref="DefaultTestStartInstant"/>.
    /// </remarks>
    public static WebApplicationFactory<TEntryPoint> WithFakeTimeProvider<TEntryPoint>(
        this WebApplicationFactory<TEntryPoint> factory,
        DateTimeOffset startInstant,
        out FakeTimeProvider fakeTimeProvider)
        where TEntryPoint : class
    {
        var fake = new FakeTimeProvider(startInstant);
        fakeTimeProvider = fake;
        return factory.WithFakeTimeProvider(fake);
    }
}