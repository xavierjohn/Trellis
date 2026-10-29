namespace Trellis.Asp.Authorization.Tests;

using System.Threading.Tasks.Sources;
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
        var completion = new TaskCompletionSource<Maybe<ActorId>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var provider = new DelegateProvider(_ =>
        {
            calls++;
            return new ValueTask<Maybe<ActorId>>(completion.Task);
        });
        var cache = new CachingActorProvider(provider, new HttpContextAccessor());
        var first = cache.GetCurrentActorIdAsync(Token).AsTask();
        var second = cache.GetCurrentActorIdAsync(Token).AsTask();
        calls.Should().Be(1);
        provider.ActorCalls.Should().Be(0);
        completion.SetResult(Maybe.From(ActorId.Create("shared")));

        (await first).Should().HaveValueEqualTo(ActorId.Create("shared"));
        (await second).Should().HaveValueEqualTo(ActorId.Create("shared"));
    }

    [Fact]
    public async Task GetCurrentActorIdAsync_SingleConsumptionValueTask_ConvertsAndConsumesOnlyOnce()
    {
        var source = new SingleConsumptionIdentitySource();
        var provider = new DelegateProvider(_ => source.CreateValueTask());
        var cache = new CachingActorProvider(provider, new HttpContextAccessor());
        var first = cache.GetCurrentActorIdAsync(Token).AsTask();
        var second = cache.GetCurrentActorIdAsync(Token).AsTask();
        source.Complete(ActorId.Create("single-use"));

        (await first).Unwrap().Value.Should().Be("single-use");
        (await second).Unwrap().Value.Should().Be("single-use");
        (await cache.GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be("single-use");
        source.Consumptions.Should().Be(1);
        provider.ActorCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentActorIdAsync_RequestAborted_CachesCancellationWithoutRetry()
    {
        using var request = new CancellationTokenSource();
        request.Cancel();
        var calls = 0;
        var provider = new DelegateProvider(token =>
        {
            calls++;
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Maybe<ActorId>.None);
        });
        var cache = new CachingActorProvider(provider, new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { RequestAborted = request.Token },
        });
        for (var i = 0; i < 2; i++)
        {
            var act = async () => await cache.GetCurrentActorIdAsync(Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        calls.Should().Be(1);
        provider.ActorCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentActorIdAsync_CallerCancellation_DoesNotCancelSharedRequestResolution()
    {
        using var requestCancellation = new CancellationTokenSource();
        using var callerCancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource<Maybe<ActorId>>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        var calls = 0;
        var provider = new DelegateProvider(token =>
        {
            observed = token;
            calls++;
            return new ValueTask<Maybe<ActorId>>(completion.Task);
        });
        var cache = new CachingActorProvider(provider, new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { RequestAborted = requestCancellation.Token },
        });
        var cancelled = cache.GetCurrentActorIdAsync(callerCancellation.Token).AsTask();
        var survivor = cache.GetCurrentActorIdAsync(Token).AsTask();
        callerCancellation.Cancel();
        var act = async () => await cancelled;
        await act.Should().ThrowAsync<OperationCanceledException>();
        observed.Should().Be(requestCancellation.Token);
        observed.IsCancellationRequested.Should().BeFalse();
        completion.SetResult(Maybe.From(ActorId.Create("survivor")));
        (await survivor).Unwrap().Value.Should().Be("survivor");
        calls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetCurrentActorIdAsync_ProviderFailure_CachesWithoutRetrying(bool synchronous)
    {
        var calls = 0;
        var failure = new InvalidOperationException("identity failure");
        var provider = new DelegateProvider(_ =>
        {
            calls++;
            if (synchronous)
                throw failure;
            return ValueTask.FromException<Maybe<ActorId>>(failure);
        });
        var cache = new CachingActorProvider(provider, new HttpContextAccessor());

        for (var i = 0; i < 2; i++)
        {
            var act = async () => await cache.GetCurrentActorIdAsync(Token);
            (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        }

        calls.Should().Be(1);
        provider.ActorCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetCurrentActorIdAsync_None_IsCachedIndependentlyOfFullActor()
    {
        var calls = 0;
        var provider = new DelegateProvider(_ =>
        {
            calls++;
            return ValueTask.FromResult(Maybe<ActorId>.None);
        });
        var cache = new CachingActorProvider(provider, new HttpContextAccessor());

        (await cache.GetCurrentActorIdAsync(Token)).Should().BeNone();
        (await cache.GetCurrentActorIdAsync(Token)).Should().BeNone();
        provider.ActorCalls.Should().Be(0);
        calls.Should().Be(1);
        (await cache.GetCurrentActorAsync(Token)).Should().BeNone();
    }

    private sealed class SingleConsumptionIdentitySource : IValueTaskSource<Maybe<ActorId>>
    {
        private ManualResetValueTaskSourceCore<Maybe<ActorId>> _source = new() { RunContinuationsAsynchronously = true };
        public int Consumptions { get; private set; }
        public ValueTask<Maybe<ActorId>> CreateValueTask() => new(this, _source.Version);
        public void Complete(ActorId id) => _source.SetResult(Maybe.From(id));

        public Maybe<ActorId> GetResult(short token)
        {
            Consumptions++;
            if (Consumptions > 1)
                throw new InvalidOperationException("This ValueTask may only be consumed once.");
            return _source.GetResult(token);
        }

        public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _source.OnCompleted(continuation, state, token, flags);
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