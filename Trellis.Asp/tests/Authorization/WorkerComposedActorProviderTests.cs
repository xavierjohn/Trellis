namespace Trellis.Asp.Authorization.Tests;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Trellis.Authorization;

public class WorkerComposedActorProviderTests
{
    private static readonly Actor _systemActor = Actor.Create(
        id: "system",
        permissions: new HashSet<string> { "workers:run" });

    private static readonly Actor _innerActor = Actor.Create(
        id: "inner-user",
        permissions: new HashSet<string> { "orders:read" });

    [Fact]
    public async Task Dispose_WithAsyncOnlyInner_DoesNotDeadlockAndLogsWarning()
    {
        var asyncOnlyInner = new AsyncOnlyDisposable(yieldToCurrentContext: true);
        var fakeLogger = new TestLogger();
        var provider = CreateProvider(asyncOnlyInner, fakeLogger);
        _ = ((IDecoratingActorProvider)provider).Inner;

        var task = Task.Run(() =>
        {
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            try
            {
                provider.Dispose();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }, TestContext.Current.CancellationToken);

        await task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        fakeLogger.WarningCount.Should().Be(1);
        fakeLogger.Messages.Should().ContainSingle(message => message.Contains("SKIPPED"));
        asyncOnlyInner.WasDisposedAsync.Should().BeFalse("sync Dispose intentionally skips async-only inners");
    }

    [Fact]
    public async Task DisposeAsync_WithAsyncOnlyInner_CorrectlyDisposes()
    {
        var asyncOnlyInner = new AsyncOnlyDisposable();
        var provider = CreateProvider(asyncOnlyInner);
        _ = ((IDecoratingActorProvider)provider).Inner;

        await provider.DisposeAsync();

        asyncOnlyInner.WasDisposedAsync.Should().BeTrue();
    }

    private static WorkerComposedActorProvider CreateProvider(
        IActorProvider inner,
        ILogger<WorkerComposedActorProvider>? logger = null) =>
        new(
            () => inner,
            ownsInner: true,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            _systemActor,
            logger);

    private sealed class AsyncOnlyDisposable : IActorProvider, IAsyncDisposable
    {
        private readonly bool _yieldToCurrentContext;

        public AsyncOnlyDisposable(bool yieldToCurrentContext = false) =>
            _yieldToCurrentContext = yieldToCurrentContext;

        public bool WasDisposedAsync { get; private set; }

        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(Maybe.From(_innerActor));
        }

        public async ValueTask DisposeAsync()
        {
            if (_yieldToCurrentContext)
                await Task.Yield();

            WasDisposedAsync = true;
        }
    }

    private sealed class TestLogger : ILogger<WorkerComposedActorProvider>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public int WarningCount => _entries.Count(e => e.Level == LogLevel.Warning);

        public IReadOnlyCollection<string> Messages => _entries.Select(e => e.Message).ToArray();

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _entries.Add((logLevel, formatter(state, exception)));

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }
    }
}