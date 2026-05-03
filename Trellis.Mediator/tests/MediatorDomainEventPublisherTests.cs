namespace Trellis.Mediator.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Tests for <see cref="MediatorDomainEventPublisher"/>.
/// </summary>
public class MediatorDomainEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_DispatchesToAllHandlersForExactRuntimeType()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);
        var first = new RecordingHandlerA();
        var second = new RecordingHandlerA();
        services.AddSingleton<IDomainEventHandler<TestEventA>>(first);
        services.AddSingleton<IDomainEventHandler<TestEventA>>(second);

        var provider = services.BuildServiceProvider();
        var publisher = new MediatorDomainEventPublisher(
            provider,
            NullLogger<MediatorDomainEventPublisher>.Instance);

        var evt = new TestEventA("payload", DateTimeOffset.UtcNow);
        await publisher.PublishAsync(evt, CancellationToken.None);

        first.Received.Should().ContainSingle().Which.Should().BeSameAs(evt);
        second.Received.Should().ContainSingle().Which.Should().BeSameAs(evt);
    }

    [Fact]
    public async Task PublishAsync_NoHandlersRegistered_IsNoOp()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);
        var provider = services.BuildServiceProvider();
        var publisher = new MediatorDomainEventPublisher(
            provider,
            NullLogger<MediatorDomainEventPublisher>.Instance);

        var act = async () => await publisher.PublishAsync(
            new TestEventA("payload", DateTimeOffset.UtcNow),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_FirstHandlerThrows_LogsAndContinuesWithRemaining()
    {
        var captureLogger = new CaptureLogger();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDomainEventHandler<TestEventA>, ThrowingHandlerA>();
        var second = new RecordingHandlerA();
        services.AddSingleton<IDomainEventHandler<TestEventA>>(second);

        var provider = services.BuildServiceProvider();
        var publisher = new MediatorDomainEventPublisher(provider, captureLogger);

        var evt = new TestEventA("payload", DateTimeOffset.UtcNow);
        await publisher.PublishAsync(evt, CancellationToken.None);

        second.Received.Should().ContainSingle("a thrown handler must not block the others");
        captureLogger.Records.Should().Contain(r =>
            r.Level == LogLevel.Error
            && r.Message.Contains("threw for event", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishAsync_HandlerOnlyMatchesExactRuntimeType()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);
        var baseHandler = new RecordingBaseHandler();
        services.AddSingleton<IDomainEventHandler<IDomainEvent>>(baseHandler);

        var provider = services.BuildServiceProvider();
        var publisher = new MediatorDomainEventPublisher(
            provider,
            NullLogger<MediatorDomainEventPublisher>.Instance);

        await publisher.PublishAsync(
            new TestEventA("payload", DateTimeOffset.UtcNow),
            CancellationToken.None);

        baseHandler.Received.Should().BeEmpty(
            "v1 dispatches by exact runtime type only — base-type handlers are not invoked");
    }

    [Fact]
    public async Task PublishAsync_NullEvent_Throws()
    {
        var services = new ServiceCollection();
        AddNullLogging(services);
        var provider = services.BuildServiceProvider();
        var publisher = new MediatorDomainEventPublisher(
            provider,
            NullLogger<MediatorDomainEventPublisher>.Instance);

        var act = async () => await publisher.PublishAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishAsync_SynchronousHandlerOCE_PropagatesAsCancellation()
    {
        // A non-async handler that synchronously throws OperationCanceledException
        // must propagate the cancellation, not be wrapped in TargetInvocationException
        // and swallowed.
        var services = new ServiceCollection();
        AddNullLogging(services);
        services.AddSingleton<IDomainEventHandler<TestEventA>, SynchronousCancellingHandler>();

        var provider = services.BuildServiceProvider();
        var publisher = new MediatorDomainEventPublisher(
            provider,
            NullLogger<MediatorDomainEventPublisher>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await publisher.PublishAsync(
            new TestEventA("payload", DateTimeOffset.UtcNow),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "MethodInfo.Invoke wraps synchronous handler exceptions in TargetInvocationException; the publisher must unwrap so cancellation isn't logged + swallowed.");
    }

    private sealed class SynchronousCancellingHandler : IDomainEventHandler<TestEventA>
    {
        public ValueTask HandleAsync(TestEventA domainEvent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private static void AddNullLogging(IServiceCollection services)
    {
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    }

    private sealed class RecordingBaseHandler : IDomainEventHandler<IDomainEvent>
    {
        public List<IDomainEvent> Received { get; } = [];

        public ValueTask HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            Received.Add(domainEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CaptureLogger : ILogger<MediatorDomainEventPublisher>
    {
        public List<(LogLevel Level, string Message)> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Records.Add((logLevel, formatter(state, exception)));
    }
}

