namespace Trellis.Testing;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

/// <summary>
/// Extension methods for <see cref="WebApplicationFactory{TEntryPoint}"/>
/// that simplify controlling time in integration tests.
/// </summary>
public static class WebApplicationFactoryTimeExtensions
{
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
    /// var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
    /// _factory = _factory.WithFakeTimeProvider(fakeTime);
    ///
    /// fakeTime.SetUtcNow(DateTimeOffset.UtcNow.AddDays(-8));
    /// await client.PostAsync("/api/orders/1/submission", null, ct);
    ///
    /// fakeTime.SetUtcNow(DateTimeOffset.UtcNow);
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
    /// registered, and outputs the provider for test use.
    /// </summary>
    /// <typeparam name="TEntryPoint">The entry point class of the web application under test.</typeparam>
    /// <param name="factory">The web application factory.</param>
    /// <param name="fakeTimeProvider">When this method returns, contains the <see cref="FakeTimeProvider"/> instance.</param>
    /// <returns>A new factory with the fake time provider registered.</returns>
    public static WebApplicationFactory<TEntryPoint> WithFakeTimeProvider<TEntryPoint>(
        this WebApplicationFactory<TEntryPoint> factory,
        out FakeTimeProvider fakeTimeProvider)
        where TEntryPoint : class
    {
        var fake = new FakeTimeProvider(DateTimeOffset.UtcNow);
        fakeTimeProvider = fake;
        return factory.WithFakeTimeProvider(fake);
    }
}
