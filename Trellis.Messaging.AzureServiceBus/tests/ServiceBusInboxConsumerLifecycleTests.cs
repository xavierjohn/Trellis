namespace Trellis.Messaging.AzureServiceBus.Tests;

using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trellis.Mediator;

public sealed class ServiceBusInboxConsumerLifecycleTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task StopAsync_ConcurrentCalls_StopAndDisposeEachProcessorOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var processor = new ControlledProcessor(blockStop: true);
        var client = new ControlledClient(processor);
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var consumer = CreateConsumer(client, provider, "orders");
        await consumer.StartAsync(cancellationToken);
        await processor.Started.Task.WaitAsync(Patience, cancellationToken);

        var firstStop = consumer.StopAsync(cancellationToken);
        await processor.Stopping.Task.WaitAsync(Patience, cancellationToken);
        var secondStop = consumer.StopAsync(cancellationToken);
        processor.ReleaseStop();
        await Task.WhenAll(firstStop, secondStop).WaitAsync(Patience, cancellationToken);
        await consumer.StopAsync(cancellationToken);

        processor.StopCalls.Should().Be(1);
        processor.CloseCalls.Should().Be(1);
    }

    [Fact]
    public async Task StopAsync_DuringStartup_WaitsForAllProcessorsBeforeDisposing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = new ControlledProcessor(blockStart: true);
        var second = new ControlledProcessor();
        var client = new ControlledClient(first, second);
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var consumer = CreateConsumer(client, provider, "orders", "invoices");
        await consumer.StartAsync(cancellationToken);
        await first.Started.Task.WaitAsync(Patience, cancellationToken);

        var stop = consumer.StopAsync(cancellationToken);
        var stoppedBeforeStartupFinished = first.StopCalls;
        first.ReleaseStart();
        await stop.WaitAsync(Patience, cancellationToken);

        stoppedBeforeStartupFinished.Should().Be(0);
        first.StopCalls.Should().Be(1);
        first.CloseCalls.Should().Be(1);
        second.StopCalls.Should().Be(1);
        second.CloseCalls.Should().Be(1);
    }

    [Fact]
    public async Task StopAsync_BeforeStart_CompletesWithoutCreatingProcessors()
    {
        var client = new ControlledClient();
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var consumer = CreateConsumer(client, provider, "orders");

        await consumer.StopAsync(TestContext.Current.CancellationToken)
            .WaitAsync(Patience, TestContext.Current.CancellationToken);

        client.CreatedProcessors.Should().Be(0);
    }

    [Fact]
    public async Task StopAsync_CancelledFirstCallerBeforeStart_CachesCancellationWithoutCreatingProcessors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = new ControlledClient();
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var consumer = CreateConsumer(client, provider, "orders");
        using var shutdownCancellation = new CancellationTokenSource();
        await shutdownCancellation.CancelAsync();
        var firstStop = async () => await consumer.StopAsync(shutdownCancellation.Token)
            .WaitAsync(Patience, cancellationToken);
        var laterStop = async () => await consumer.StopAsync(CancellationToken.None)
            .WaitAsync(Patience, cancellationToken);

        await firstStop.Should().ThrowAsync<OperationCanceledException>();
        var failure = await laterStop.Should().ThrowAsync<OperationCanceledException>();
        failure.Which.CancellationToken.Should().Be(shutdownCancellation.Token);
        client.CreatedProcessors.Should().Be(0);
    }

    [Fact]
    public async Task StopAsync_StartupCancelledBeforeExecution_CompletesWithoutCreatingProcessors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var client = new ControlledClient();
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var consumer = CreateConsumer(client, provider, "orders");
        using var startupCancellation = new CancellationTokenSource();
        await startupCancellation.CancelAsync();
        await consumer.StartAsync(startupCancellation.Token);
        var execution = async () => await consumer.ExecuteTask!.WaitAsync(Patience, cancellationToken);
        await execution.Should().ThrowAsync<OperationCanceledException>();

        await consumer.StopAsync(CancellationToken.None).WaitAsync(Patience, cancellationToken);

        client.CreatedProcessors.Should().Be(0);
    }

    [Fact]
    public async Task StopAsync_StartupFailure_DisposesTheCreatedProcessor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var processor = new ControlledProcessor { StartFailure = new InvalidOperationException("startup failed") };
        var client = new ControlledClient(processor);
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var consumer = CreateConsumer(client, provider, "orders");
        await consumer.StartAsync(cancellationToken);
        var execution = async () => await consumer.ExecuteTask!.WaitAsync(Patience, cancellationToken);
        await execution.Should().ThrowAsync<InvalidOperationException>().WithMessage("startup failed");

        await consumer.StopAsync(cancellationToken).WaitAsync(Patience, cancellationToken);

        processor.StopCalls.Should().Be(1);
        processor.CloseCalls.Should().Be(1);
    }

    [Fact]
    public async Task StopAsync_ProcessorFailure_DisposesAllProcessorsAndPropagatesTheFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = new ControlledProcessor { StopFailure = new InvalidOperationException("shutdown failed") };
        var second = new ControlledProcessor();
        var client = new ControlledClient(first, second);
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var consumer = CreateConsumer(client, provider, "orders", "invoices");
        await consumer.StartAsync(cancellationToken);
        await second.Started.Task.WaitAsync(Patience, cancellationToken);
        var stop = async () => await consumer.StopAsync(cancellationToken).WaitAsync(Patience, cancellationToken);

        await stop.Should().ThrowAsync<InvalidOperationException>().WithMessage("shutdown failed");
        await stop.Should().ThrowAsync<InvalidOperationException>().WithMessage("shutdown failed");

        first.StopCalls.Should().Be(1);
        first.CloseCalls.Should().Be(1);
        second.StopCalls.Should().Be(1);
        second.CloseCalls.Should().Be(1);
        consumer.ExecuteTask!.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_DisposalFailure_DisposesAllProcessorsAndCachesTheFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var closeFailure = new InvalidOperationException("disposal failed");
        var first = new ControlledProcessor { CloseFailure = closeFailure };
        var second = new ControlledProcessor();
        var client = new ControlledClient(first, second);
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var consumer = CreateConsumer(client, provider, "orders", "invoices");
        await consumer.StartAsync(cancellationToken);
        await second.Started.Task.WaitAsync(Patience, cancellationToken);
        var stop = async () => await consumer.StopAsync(cancellationToken).WaitAsync(Patience, cancellationToken);

        var failure = await stop.Should().ThrowAsync<InvalidOperationException>();
        failure.Which.Should().BeSameAs(closeFailure);
        var cachedFailure = await stop.Should().ThrowAsync<InvalidOperationException>();
        cachedFailure.Which.Should().BeSameAs(closeFailure);
        first.StopCalls.Should().Be(1);
        first.CloseCalls.Should().Be(1);
        second.StopCalls.Should().Be(1);
        second.CloseCalls.Should().Be(1);
        consumer.ExecuteTask!.IsCompleted.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopAsync_StopAndDisposalFailures_DisposesAllProcessorsAndCachesBothFailures(
        bool synchronousStopFailure)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stopFailure = new InvalidOperationException("shutdown failed");
        var closeFailure = new InvalidOperationException("disposal failed");
        var first = new ControlledProcessor
        {
            StopFailure = stopFailure,
            ThrowStopFailureSynchronously = synchronousStopFailure,
            CloseFailure = closeFailure,
        };
        var second = new ControlledProcessor();
        var client = new ControlledClient(first, second);
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var consumer = CreateConsumer(client, provider, "orders", "invoices");
        await consumer.StartAsync(cancellationToken);
        await second.Started.Task.WaitAsync(Patience, cancellationToken);
        var stop = async () => await consumer.StopAsync(cancellationToken).WaitAsync(Patience, cancellationToken);

        var failure = await stop.Should().ThrowAsync<AggregateException>();
        failure.Which.InnerExceptions.Should().Equal(stopFailure, closeFailure);
        var cachedFailure = await stop.Should().ThrowAsync<AggregateException>();
        cachedFailure.Which.Should().BeSameAs(failure.Which);
        first.StopCalls.Should().Be(1);
        first.CloseCalls.Should().Be(1);
        second.StopCalls.Should().Be(1);
        second.CloseCalls.Should().Be(1);
        consumer.ExecuteTask!.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_CancellationAfterProcessorStop_CachesCancellation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var shutdownCancellation = new CancellationTokenSource();
        var processor = new ControlledProcessor { OnClose = shutdownCancellation.Cancel };
        var client = new ControlledClient(processor);
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var consumer = CreateConsumer(client, provider, "orders");
        await consumer.StartAsync(cancellationToken);
        await processor.Started.Task.WaitAsync(Patience, cancellationToken);
        var firstStop = async () => await consumer.StopAsync(shutdownCancellation.Token)
            .WaitAsync(Patience, cancellationToken);
        var laterStop = async () => await consumer.StopAsync(CancellationToken.None)
            .WaitAsync(Patience, cancellationToken);

        await firstStop.Should().ThrowAsync<OperationCanceledException>();
        var failure = await laterStop.Should().ThrowAsync<OperationCanceledException>();
        failure.Which.CancellationToken.Should().Be(shutdownCancellation.Token);
        processor.StopCalls.Should().Be(1);
        processor.CloseCalls.Should().Be(1);
    }

    [Fact]
    public async Task StopAsync_ProcessorFailureAndLateCancellation_PreservesTheProcessorFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var shutdownCancellation = new CancellationTokenSource();
        var stopFailure = new InvalidOperationException("shutdown failed");
        var processor = new ControlledProcessor
        {
            StopFailure = stopFailure,
            OnClose = shutdownCancellation.Cancel,
        };
        var client = new ControlledClient(processor);
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var consumer = CreateConsumer(client, provider, "orders");
        await consumer.StartAsync(cancellationToken);
        await processor.Started.Task.WaitAsync(Patience, cancellationToken);
        var firstStop = async () => await consumer.StopAsync(shutdownCancellation.Token)
            .WaitAsync(Patience, cancellationToken);
        var laterStop = async () => await consumer.StopAsync(CancellationToken.None)
            .WaitAsync(Patience, cancellationToken);

        await firstStop.Should().ThrowAsync<Exception>()
            .Where(exception => exception is OperationCanceledException || ReferenceEquals(exception, stopFailure));
        var cachedFailure = await laterStop.Should().ThrowAsync<InvalidOperationException>();
        cachedFailure.Which.Should().BeSameAs(stopFailure);
        processor.StopCalls.Should().Be(1);
        processor.CloseCalls.Should().Be(1);
    }

    [Fact]
    public async Task StopAsync_CancelledConcurrentCaller_DoesNotCancelTheSharedShutdown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var processor = new ControlledProcessor(blockStop: true);
        var client = new ControlledClient(processor);
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var consumer = CreateConsumer(client, provider, "orders");
        await consumer.StartAsync(cancellationToken);
        await processor.Started.Task.WaitAsync(Patience, cancellationToken);
        var firstStop = consumer.StopAsync(cancellationToken);
        await processor.Stopping.Task.WaitAsync(Patience, cancellationToken);
        using var callerCancellation = new CancellationTokenSource();
        var secondStop = consumer.StopAsync(callerCancellation.Token);
        await callerCancellation.CancelAsync();
        var cancelledWait = async () => await secondStop;

        try
        {
            await cancelledWait.Should().ThrowAsync<OperationCanceledException>();
            firstStop.IsCompleted.Should().BeFalse();
        }
        finally
        {
            processor.ReleaseStop();
            await firstStop.WaitAsync(Patience, cancellationToken);
        }

        processor.StopCalls.Should().Be(1);
        processor.CloseCalls.Should().Be(1);
    }

    [Fact]
    public async Task StopAsync_CancelledFirstCaller_DisposesOnceAndCachesCancellation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var processor = new ControlledProcessor(blockStop: true);
        var client = new ControlledClient(processor);
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var consumer = CreateConsumer(client, provider, "orders");
        await consumer.StartAsync(cancellationToken);
        await processor.Started.Task.WaitAsync(Patience, cancellationToken);
        using var shutdownCancellation = new CancellationTokenSource();
        var firstStop = consumer.StopAsync(shutdownCancellation.Token);
        await processor.Stopping.Task.WaitAsync(Patience, cancellationToken);
        var secondStop = consumer.StopAsync(CancellationToken.None);
        await shutdownCancellation.CancelAsync();
        var cancelledFirstWait = async () => await firstStop.WaitAsync(Patience, cancellationToken);
        var cancelledSharedShutdown = async () => await secondStop.WaitAsync(Patience, cancellationToken);

        await cancelledFirstWait.Should().ThrowAsync<OperationCanceledException>();
        await cancelledSharedShutdown.Should().ThrowAsync<OperationCanceledException>();
        var laterStop = async () => await consumer.StopAsync(CancellationToken.None)
            .WaitAsync(Patience, cancellationToken);
        await laterStop.Should().ThrowAsync<OperationCanceledException>();

        processor.StopCalls.Should().Be(1);
        processor.CloseCalls.Should().Be(1);
    }

    [Fact]
    public async Task StopAsync_CancellationDuringStartup_EventuallyDisposesTheCreatedProcessor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var processor = new ControlledProcessor(blockStart: true);
        var client = new ControlledClient(processor);
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var consumer = CreateConsumer(client, provider, "orders");
        await consumer.StartAsync(cancellationToken);
        await processor.Started.Task.WaitAsync(Patience, cancellationToken);
        using var shutdownCancellation = new CancellationTokenSource();
        var stop = consumer.StopAsync(shutdownCancellation.Token);
        await shutdownCancellation.CancelAsync();
        var cancelledWait = async () => await stop;

        await cancelledWait.Should().ThrowAsync<OperationCanceledException>();
        await processor.Closed.Task.WaitAsync(Patience, cancellationToken);

        processor.StopCalls.Should().Be(1);
        processor.CloseCalls.Should().Be(1);
    }

    private static ServiceBusInboxConsumer CreateConsumer(
        ServiceBusClient client, IServiceProvider services, params string[] topics)
    {
        var options = new AzureServiceBusConsumerOptions();
        foreach (var topic in topics)
            options.Subscribe(topic, "billing");

        return new ServiceBusInboxConsumer(
            client, services.GetRequiredService<IServiceScopeFactory>(), IntegrationEventNameMap.Empty, Options.Create(options),
            NullLogger<ServiceBusInboxConsumer>.Instance);
    }

    private sealed class ControlledClient(params ControlledProcessor[] processors) : ServiceBusClient
    {
        public int CreatedProcessors { get; private set; }

        public override ServiceBusProcessor CreateProcessor(
            string topicName, string subscriptionName, ServiceBusProcessorOptions options) =>
            processors[CreatedProcessors++];
    }

    private sealed class ControlledProcessor : ServiceBusProcessor
    {
        private readonly TaskCompletionSource _start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _stopCalls;
        private int _closeCalls;

        public ControlledProcessor(bool blockStart = false, bool blockStop = false)
        {
            if (!blockStart)
                _start.SetResult();
            if (!blockStop)
                _stop.SetResult();
        }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopping { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StopCalls => Volatile.Read(ref _stopCalls);
        public int CloseCalls => Volatile.Read(ref _closeCalls);
        public InvalidOperationException? StartFailure { get; init; }
        public InvalidOperationException? StopFailure { get; init; }
        public bool ThrowStopFailureSynchronously { get; init; }
        public InvalidOperationException? CloseFailure { get; init; }
        public Action? OnClose { get; init; }

        public void ReleaseStart() => _start.TrySetResult();
        public void ReleaseStop() => _stop.TrySetResult();

        public override Task StartProcessingAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            if (StartFailure is not null)
                return Task.FromException(StartFailure);
            return _start.Task.WaitAsync(cancellationToken);
        }

        public override Task StopProcessingAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _stopCalls);
            Stopping.TrySetResult();
            if (StopFailure is not null)
            {
                if (ThrowStopFailureSynchronously)
                    throw StopFailure;
                return Task.FromException(StopFailure);
            }

            return _stop.Task.WaitAsync(cancellationToken);
        }

        public override Task CloseAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _closeCalls);
            Closed.TrySetResult();
            OnClose?.Invoke();
            if (CloseFailure is not null)
                return Task.FromException(CloseFailure);
            return Task.CompletedTask;
        }
    }
}