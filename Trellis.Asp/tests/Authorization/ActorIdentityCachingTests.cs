namespace Trellis.Asp.Authorization.Tests;

using Microsoft.AspNetCore.Http;
using Trellis.Authorization;
using Trellis.Testing;

public class ActorIdentityCachingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetCurrentActorOrIdentityAsync_ConcurrentInitialization_InvokesProviderOnce(bool identity)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var provider = new DelegateProvider(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                entered.SetResult();
            else
                secondEntered.TrySetResult();
            release.Wait(Token);
            return ValueTask.FromResult(Maybe<ActorId>.None);
        });
        var cache = new CachingActorProvider(provider, new HttpContextAccessor());
        Func<Task> resolve = identity
            ? async () => _ = await cache.GetCurrentActorIdAsync(Token)
            : async () => _ = await cache.GetCurrentActorAsync(Token);
        var first = Task.Factory.StartNew(resolve,
            Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        Task? second = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            second = Task.Factory.StartNew(resolve,
                Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            await Task.WhenAny(secondEntered.Task, Task.Delay(TimeSpan.FromSeconds(1), Token));
        }
        finally
        {
            release.Set();
            await first;
            if (second is not null)
                await second;
        }

        calls.Should().Be(1, "concurrent callers must share one resolution, including synchronous initialization");
    }

    [Fact]
    public async Task GetCurrentActorIdAsync_InFlightValueTask_CallersShareOneTaskWithoutLoadingActor()
    {
        var completion = new ControlledAsync<Maybe<ActorId>>();
        var calls = new CallbackRecorder<CancellationToken>();
        var provider = new DelegateProvider(calls.Wrap(_ => completion.ValueTask));
        var cache = new CachingActorProvider(provider, new HttpContextAccessor());
        var first = cache.GetCurrentActorIdAsync(Token).AsTask();
        var second = cache.GetCurrentActorIdAsync(Token).AsTask();
        calls.CallCount.Should().Be(1);
        provider.ActorCalls.Should().Be(0);
        completion.TryComplete(Maybe.From(ActorId.Create("shared")));

        (await first).Should().HaveValueEqualTo(ActorId.Create("shared"));
        (await second).Should().HaveValueEqualTo(ActorId.Create("shared"));
    }

    [Fact]
    public async Task GetCurrentActorIdAsync_SingleConsumptionValueTask_ConvertsAndConsumesOnlyOnce()
    {
        var source = ControlledAsync.SingleUse<Maybe<ActorId>>();
        var provider = new DelegateProvider(_ => source.ValueTask);
        var cache = new CachingActorProvider(provider, new HttpContextAccessor());
        var first = cache.GetCurrentActorIdAsync(Token).AsTask();
        var second = cache.GetCurrentActorIdAsync(Token).AsTask();
        source.TryComplete(Maybe.From(ActorId.Create("single-use")));

        (await first).Unwrap().Value.Should().Be("single-use");
        (await second).Unwrap().Value.Should().Be("single-use");
        (await cache.GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be("single-use");
        source.ResultReadCount.Should().Be(1);
        provider.ActorCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentActorIdAsync_RequestAborted_CachesCancellationWithoutRetry()
    {
        using var request = CanceledToken.Create();
        var calls = new CallbackRecorder<CancellationToken>();
        var provider = new DelegateProvider(calls.Wrap(token =>
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Maybe<ActorId>.None);
        }));
        var cache = new CachingActorProvider(provider, new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { RequestAborted = request.Token },
        });
        for (var i = 0; i < 2; i++)
        {
            var act = async () => await cache.GetCurrentActorIdAsync(Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        calls.CallCount.Should().Be(1);
        provider.ActorCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentActorIdAsync_CallerCancellation_DoesNotCancelSharedRequestResolution()
    {
        using var requestCancellation = new CancellationTokenSource();
        using var callerCancellation = new CancellationTokenSource();
        var completion = new ControlledAsync<Maybe<ActorId>>();
        var calls = new CallbackRecorder<CancellationToken>();
        var provider = new DelegateProvider(calls.Wrap(_ => completion.ValueTask));
        var cache = new CachingActorProvider(provider, new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { RequestAborted = requestCancellation.Token },
        });
        var cancelled = cache.GetCurrentActorIdAsync(callerCancellation.Token).AsTask();
        var survivor = cache.GetCurrentActorIdAsync(Token).AsTask();
        callerCancellation.Cancel();
        var act = async () => await cancelled;
        await act.Should().ThrowAsync<OperationCanceledException>();
        var observed = calls.Arguments.Should().ContainSingle().Which;
        observed.Should().Be(requestCancellation.Token);
        observed.IsCancellationRequested.Should().BeFalse();
        completion.TryComplete(Maybe.From(ActorId.Create("survivor")));
        (await survivor).Unwrap().Value.Should().Be("survivor");
        calls.CallCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetCurrentActorIdAsync_ProviderFailure_CachesWithoutRetrying(bool synchronous)
    {
        var calls = new CallbackRecorder<CancellationToken>();
        var failure = new InvalidOperationException("identity failure");
        var provider = new DelegateProvider(calls.Wrap(_ =>
        {
            if (synchronous)
                throw failure;
            return ValueTask.FromException<Maybe<ActorId>>(failure);
        }));
        var cache = new CachingActorProvider(provider, new HttpContextAccessor());

        for (var i = 0; i < 2; i++)
        {
            var act = async () => await cache.GetCurrentActorIdAsync(Token);
            (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        }

        calls.CallCount.Should().Be(1);
        provider.ActorCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentActorIdAsync_None_IsCachedIndependentlyOfFullActor()
    {
        var calls = new CallbackRecorder<CancellationToken>();
        var provider = new DelegateProvider(calls.Wrap(_ => ValueTask.FromResult(Maybe<ActorId>.None)));
        var cache = new CachingActorProvider(provider, new HttpContextAccessor());

        (await cache.GetCurrentActorIdAsync(Token)).Should().BeNone();
        (await cache.GetCurrentActorIdAsync(Token)).Should().BeNone();
        provider.ActorCalls.Should().Be(0);
        calls.CallCount.Should().Be(1);
        (await cache.GetCurrentActorAsync(Token)).Should().BeNone();
    }

    private sealed class DelegateProvider(
        Func<CancellationToken, ValueTask<Maybe<ActorId>>> resolve) : IActorProvider, IActorIdentityProvider
    {
        public int ActorCalls { get; private set; }

        public ValueTask<Maybe<ActorId>> GetCurrentActorIdAsync(CancellationToken cancellationToken = default) =>
            resolve(cancellationToken);

        public async Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            ActorCalls++;
            _ = await resolve(cancellationToken);
            return Maybe<Actor>.None;
        }
    }
}