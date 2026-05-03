namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// End-to-end test that exercises the full chain: pipeline behavior resolved from DI,
/// real <see cref="MediatorDomainEventPublisher"/>, and DI-resolved handler.
/// This complements the unit tests by proving the wiring composes correctly.
/// </summary>
public class DomainEventDispatchIntegrationTests
{
    [Fact]
    public async Task FullChain_DispatchesEventsThroughDIResolvedHandlers()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDomainEventHandler<TestEventA, CapturingHandler>();
        var captured = new List<TestEventA>();
        services.AddSingleton(captured);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var behavior = ActivatorUtilities.CreateInstance<
            DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>>(scope.ServiceProvider);

        var aggregate = new TestAggregate(new TestAggregateId(Guid.NewGuid()));
        var raised = new TestEventA("integration-payload", DateTimeOffset.UtcNow);
        aggregate.RaiseEvent(raised);

        var response = await behavior.Handle(
            new AggregateCommand(aggregate),
            (_, _) => new ValueTask<Result<TestAggregate>>(Result.Ok(aggregate)),
            TestContext.Current.CancellationToken);

        response.IsSuccess.Should().BeTrue();
        captured.Should().ContainSingle().Which.Should().BeSameAs(raised);
        aggregate.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task FullChain_DoesNotDispatch_WhenCommandFails()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDomainEventHandler<TestEventA, CapturingHandler>();
        var captured = new List<TestEventA>();
        services.AddSingleton(captured);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var behavior = ActivatorUtilities.CreateInstance<
            DomainEventDispatchBehavior<AggregateCommand, Result<TestAggregate>>>(scope.ServiceProvider);

        var aggregate = new TestAggregate(new TestAggregateId(Guid.NewGuid()));
        aggregate.RaiseEvent(new TestEventA("never-dispatched", DateTimeOffset.UtcNow));

        var failure = Result.Fail<TestAggregate>(
            new Error.NotFound(new ResourceRef("Aggregate", aggregate.Id.Value.ToString())));
        var response = await behavior.Handle(
            new AggregateCommand(aggregate),
            (_, _) => new ValueTask<Result<TestAggregate>>(failure),
            TestContext.Current.CancellationToken);

        response.IsFailure.Should().BeTrue();
        captured.Should().BeEmpty();
        aggregate.UncommittedEvents().Should().HaveCount(1);
    }

    private sealed class CapturingHandler : IDomainEventHandler<TestEventA>
    {
        private readonly List<TestEventA> _capturedEvents;

        public CapturingHandler(List<TestEventA> capturedEvents) => _capturedEvents = capturedEvents;

        public ValueTask HandleAsync(TestEventA domainEvent, CancellationToken cancellationToken)
        {
            _capturedEvents.Add(domainEvent);
            return ValueTask.CompletedTask;
        }
    }
}

