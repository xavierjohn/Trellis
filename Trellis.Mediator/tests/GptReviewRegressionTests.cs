namespace Trellis.Mediator.Tests;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trellis.Authorization;
using Trellis.Mediator.Tests.Helpers;

/// <summary>
/// Regression tests for the three findings surfaced by the GPT-5.5 review of
/// <c>Trellis.Mediator</c> (review of branch <c>fix/mediator-inspection</c>):
/// <list type="number">
///   <item><description>(Major) <see cref="ResourceAuthorizationBehavior{TMessage, TResource, TResponse}"/>
///   resolved the loader from DI before checking the actor — letting an unauthenticated request
///   trigger loader-side effects via the DI factory before the actor null check (ga-11
///   guarantee says "no I/O on unauthenticated requests").</description></item>
///   <item><description>(Major) <see cref="ServiceCollectionExtensions.AddResourceAuthorization(IServiceCollection, System.Reflection.Assembly[])"/>
///   silently <c>continue</c>d for security-marked messages whose <c>TResponse</c> didn't
///   satisfy the behavior constraints, shipping the command without resource authorization.</description></item>
///   <item><description>(Minor) <see cref="DomainEventDispatchBehavior{TMessage, TResponse}"/>'s
///   aggregate extractor only handled responses where <c>TResponse</c> itself was a
///   single-arg generic — it missed custom non-generic types implementing
///   <c>IResult&lt;TAggregate&gt;</c> and generics where the aggregate isn't arg #0.</description></item>
/// </list>
/// </summary>
public class GptReviewRegressionTests
{
    #region Finding #1 — loader DI factory must not run when actor is null (ga-11)

    /// <summary>
    /// GPT-5.5 review (Major): the resource-authorization behavior must not invoke the
    /// <see cref="IResourceLoader{TMessage, TResource}"/>'s DI factory when the caller is
    /// unauthenticated. Loader factories are arbitrary user code (they may open a
    /// <c>DbContext</c> or pre-fetch state during construction), so loader resolution itself
    /// counts as I/O for the ga-11 guarantee. The previous test
    /// (<c>Handle_NullActor_ThrowsBeforeResourceLoaderIsCalled</c>) only proved
    /// <c>LoadAsync</c> wasn't called — not that the factory wasn't invoked.
    /// </summary>
    [Fact]
    public async Task ResourceAuthorization_NullActor_DoesNotInvokeLoaderDIFactory()
    {
        var factoryInvocations = 0;

        var services = new ServiceCollection();
        services.AddScoped<IResourceLoader<ResourceOwnerCommand, TestResource>>(_ =>
        {
            factoryInvocations++;
            return new FakeResourceLoader<ResourceOwnerCommand>(new TestResource("res-1", "owner-1"));
        });

        var behavior = new ResourceAuthorizationBehavior<ResourceOwnerCommand, TestResource, Result<string>>(
            actorProvider: new NullActorProvider(),
            serviceProvider: services.BuildServiceProvider());

        var command = new ResourceOwnerCommand("res-1");
        var next = NextDelegate.ReturningAsync<ResourceOwnerCommand, Result<string>>(Result.Ok("Done"));

        await FluentActions.Invoking(() => behavior.Handle(command, next, CancellationToken.None).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();

        factoryInvocations.Should().Be(0,
            "ga-11 requires that no I/O — including DI factory invocations — runs when the caller is unauthenticated. "
            + "Loader factories are arbitrary user code (a custom factory could open a DbContext, query a cache, etc.).");
    }

    #endregion

    #region Finding #2 — assembly scan must fail fast on misconfigured TResponse

    /// <summary>
    /// GPT-5.5 review (Major): silently skipping a security-marked
    /// <see cref="IAuthorizeResource{TResource}"/> message because its <c>TResponse</c>
    /// doesn't satisfy <c>IResult + IFailureFactory&lt;TResponse&gt;</c> is a dangerous
    /// failure mode — the message ships without resource authorization and runtime tests
    /// may miss it. The fix moves the validation into a fail-fast helper that throws an
    /// <see cref="InvalidOperationException"/> naming the offending message + response type.
    /// Tested at the helper level so the assembly scanner's tests over the test assembly
    /// don't pollute one another.
    /// </summary>
    [Fact]
    public void Validation_ResponseTypeMissingIResult_Throws()
    {
        var act = () => ServiceCollectionExtensions.ValidateResourceAuthorizationResponseType(
            messageType: typeof(NotARealCommand),
            resourceType: typeof(TestResource),
            responseType: typeof(NotAResult));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not implement IResult*")
            .WithMessage($"*{nameof(NotARealCommand)}*")
            .WithMessage($"*{nameof(NotAResult)}*");
    }

    [Fact]
    public void Validation_ResponseTypeMissingIFailureFactory_Throws()
    {
        var act = () => ServiceCollectionExtensions.ValidateResourceAuthorizationResponseType(
            messageType: typeof(NotARealCommand),
            resourceType: typeof(TestResource),
            responseType: typeof(BareResultNoFactory));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not implement IFailureFactory*")
            .WithMessage($"*{nameof(BareResultNoFactory)}*");
    }

    [Fact]
    public void Validation_ResponseTypeSatisfiesConstraints_DoesNotThrow()
    {
        // Result<T> implements both IResult and IFailureFactory<Result<T>>.
        var act = () => ServiceCollectionExtensions.ValidateResourceAuthorizationResponseType(
            messageType: typeof(ResourceOwnerCommand),
            resourceType: typeof(TestResource),
            responseType: typeof(Result<string>));

        act.Should().NotThrow();
    }

    #endregion

    #region Finding #3 — extractor must walk IResult<T> interface, not assume it's TResponse[0]

    /// <summary>
    /// GPT-5.5 review (Minor): the aggregate extractor's previous shape
    /// (<c>responseType.GetGenericArguments()[0]</c>) silently failed for custom non-generic
    /// envelope types implementing <see cref="IResult{T}"/> where <c>T</c> is the aggregate.
    /// The fix walks the implemented interfaces looking for <c>IResult&lt;T&gt;</c> with
    /// <c>T : IAggregate</c>, so result polymorphism beyond <c>Result&lt;T&gt;</c> works as
    /// the API reference promises.
    /// </summary>
    [Fact]
    public async Task Dispatch_NonGenericResponseImplementingIResult_DispatchesEvents()
    {
        var aggregate = new TestAggregate(new TestAggregateId(Guid.NewGuid()));
        aggregate.RaiseEvent(new TestEventA("payload-1", DateTimeOffset.UtcNow));

        var publisher = new RecordingPublisher();
        var behavior = new DomainEventDispatchBehavior<NonGenericResponseCommand, NonGenericAggregateResponse>(
            publisher,
            NullLogger<DomainEventDispatchBehavior<NonGenericResponseCommand, NonGenericAggregateResponse>>.Instance);

        var response = NonGenericAggregateResponse.Success(aggregate);
        var command = new NonGenericResponseCommand();

        var result = await behavior.Handle(
            command,
            (_, _) => new ValueTask<NonGenericAggregateResponse>(response),
            CancellationToken.None);

        result.Should().BeSameAs(response);
        publisher.PublishedEvents.Should().HaveCount(1);
        publisher.PublishedEvents[0].Should().BeOfType<TestEventA>();
        aggregate.UncommittedEvents().Should().BeEmpty(
            "AcceptChanges() should run after the wave loop drains, regardless of the response shape");
    }

    #endregion

    #region Helpers

    /// <summary>Marker — never instantiated; used only for ValidateResourceAuthorizationResponseType message-type arg.</summary>
    private sealed record NotARealCommand;

    /// <summary>Response type missing <see cref="IResult"/>.</summary>
    private sealed record NotAResult;

    /// <summary>Response type implementing <see cref="IResult"/> but not <see cref="IFailureFactory{TSelf}"/>.</summary>
    private sealed record BareResultNoFactory : IResult
    {
        public bool IsSuccess => true;

        public bool IsFailure => false;

        public Error Error => throw new InvalidOperationException();

        public bool TryGetError([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Error? error)
        {
            error = null;
            return false;
        }
    }

    /// <summary>
    /// Custom non-generic envelope implementing <see cref="IResult{TestAggregate}"/> directly.
    /// The previous extractor shape (<c>typeof(TResponse).GetGenericArguments()[0]</c>) would
    /// silently fail to dispatch events for this shape because <c>typeof(NonGenericAggregateResponse)</c>
    /// is not generic.
    /// </summary>
    public sealed class NonGenericAggregateResponse : IResult<TestAggregate>
    {
        private readonly TestAggregate? _value;
        private readonly Error? _error;

        private NonGenericAggregateResponse(TestAggregate? value, Error? error)
        {
            _value = value;
            _error = error;
        }

        public static NonGenericAggregateResponse Success(TestAggregate value) => new(value, null);

        public bool IsSuccess => _value is not null;

        public bool IsFailure => _error is not null;

        public Error Error => _error
            ?? throw new InvalidOperationException("Cannot access Error on a successful result.");

        public bool TryGetError([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Error? error)
        {
            error = _error;
            return _error is not null;
        }

        public bool TryGetValue([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TestAggregate value)
        {
            value = _value;
            return _value is not null;
        }
    }

    /// <summary>
    /// Command whose <c>TResponse</c> is the custom <see cref="NonGenericAggregateResponse"/>
    /// envelope above. Drives the regression for finding #3.
    /// </summary>
    public sealed record NonGenericResponseCommand : ICommand<NonGenericAggregateResponse>;

    private sealed class RecordingPublisher : IDomainEventPublisher
    {
        public List<IDomainEvent> PublishedEvents { get; } = [];

        public ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            PublishedEvents.Add(domainEvent);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Actor provider whose <see cref="GetCurrentActorAsync"/> returns null — simulates a
    /// misbehaving <see cref="IActorProvider"/> implementation that violates the contract by
    /// returning null instead of throwing. Used by the ga-11 regression test.
    /// </summary>
    private sealed class NullActorProvider : IActorProvider
    {
        public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Actor>(null!);
    }

    /// <summary>
    /// Loader fixture used by the ga-11 regression test — captures <c>WasCalled</c> so
    /// constructor-time / DI-factory-time invocation can be distinguished from
    /// <c>LoadAsync</c>-time invocation.
    /// </summary>
    private sealed class FakeResourceLoader<TMessage> : IResourceLoader<TMessage, TestResource>
    {
        private readonly TestResource? _resource;

        public FakeResourceLoader(TestResource? resource) => _resource = resource;

        public bool WasCalled { get; private set; }

        public Task<Result<TestResource>> LoadAsync(TMessage message, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return _resource is not null
                ? Task.FromResult(Result.Ok(_resource))
                : Task.FromResult(Result.Fail<TestResource>(
                    new Error.NotFound(ResourceRef.For<TestResource>("not-found"))));
        }
    }

    #endregion
}
