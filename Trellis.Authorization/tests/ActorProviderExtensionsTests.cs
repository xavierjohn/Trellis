namespace Trellis.Authorization.Tests;

public class ActorProviderExtensionsTests
{
    [Fact]
    public async Task RequireActorAsync_PresentActor_ReturnsSameInstanceAndForwardsTokenOnce()
    {
        var actor = Actor.Create("user-1", new HashSet<string>());
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        CancellationToken received = default;
        IActorProvider provider = new DelegateActorProvider(token =>
        {
            calls++;
            received = token;
            return Task.FromResult(Maybe.From(actor));
        });

        var result = await provider.RequireActorAsync(cancellation.Token);

        result.Should().BeSameAs(actor);
        received.Should().Be(cancellation.Token);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task RequireActorAsync_DefaultToken_ForwardsNone()
    {
        var actor = Actor.Create("user-1", new HashSet<string>());
        var provider = new DelegateActorProvider(token =>
        {
            token.Should().Be(CancellationToken.None);
            return Task.FromResult(Maybe.From(actor));
        });

#pragma warning disable xUnit1051 // This test verifies the optional cancellation-token default.
        var result = await provider.RequireActorAsync();
#pragma warning restore xUnit1051

        result.Should().BeSameAs(actor);
    }

    [Fact]
    public async Task RequireActorAsync_AbsentActor_ThrowsActionableInvariantException()
    {
        var provider = new DelegateActorProvider(_ => Task.FromResult(Maybe<Actor>.None));

        var act = () => provider.RequireActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*RequireActorAsync*actor presence*");
    }

    [Fact]
    public async Task RequireActorAsync_NullProvider_ThrowsArgumentNullException()
    {
        IActorProvider provider = null!;

        var act = () => provider.RequireActorAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("actorProvider");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RequireActorAsync_DeferredResolution_WaitsForProvider(bool hasActor)
    {
        var actor = Actor.Create("user-1", new HashSet<string>());
        var completion = new TaskCompletionSource<Maybe<Actor>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new DelegateActorProvider(_ => completion.Task);

        var pending = provider.RequireActorAsync(TestContext.Current.CancellationToken);

        pending.IsCompleted.Should().BeFalse();
        completion.SetResult(hasActor ? Maybe.From(actor) : Maybe<Actor>.None);

        if (hasActor)
            (await pending).Should().BeSameAs(actor);
        else
        {
            var act = () => pending;
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*RequireActorAsync*actor presence*");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RequireActorAsync_ProviderFailure_PropagatesOriginalException(bool synchronousThrow)
    {
        var exception = new InvalidOperationException("Provider configuration failed.");
        var provider = new DelegateActorProvider(_ => synchronousThrow
            ? throw exception
            : Task.FromException<Maybe<Actor>>(exception));

        var act = () => provider.RequireActorAsync(TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(exception);
    }

    [Fact]
    public async Task RequireActorAsync_ProviderCancellation_PreservesCancellationToken()
    {
        var token = new CancellationToken(canceled: true);
        var provider = new DelegateActorProvider(Task.FromCanceled<Maybe<Actor>>);

        var act = () => provider.RequireActorAsync(token);

        (await act.Should().ThrowAsync<OperationCanceledException>())
            .Which.CancellationToken.Should().Be(token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequireActorAsync_UncachedMutableProvider_ResolvesAgainOnEveryCall(bool sameIdentity)
    {
        var first = Actor.Create("user-1", new HashSet<string>(["Read"]));
        var second = Actor.Create(sameIdentity ? "user-1" : "user-2", new HashSet<string>(["Write"]));
        var calls = 0;
        var provider = new DelegateActorProvider(_ =>
            Task.FromResult(Maybe.From(++calls == 1 ? first : second)));

        var previouslyResolved = (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).GetValueOrThrow();

        previouslyResolved.Should().BeSameAs(first);
        previouslyResolved.HasPermission("Read").Should().BeTrue();
        var firstRequired = await provider.RequireActorAsync(TestContext.Current.CancellationToken);
        var secondRequired = await provider.RequireActorAsync(TestContext.Current.CancellationToken);

        firstRequired.Should().BeSameAs(second);
        secondRequired.Should().BeSameAs(second);
        calls.Should().Be(3);
    }

    private sealed class DelegateActorProvider(Func<CancellationToken, Task<Maybe<Actor>>> resolve) : IActorProvider
    {
        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            resolve(cancellationToken);
    }
}
