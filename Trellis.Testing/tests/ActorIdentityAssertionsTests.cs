namespace Trellis.Testing.Tests;

using FluentAssertions.Execution;
using Trellis.Authorization;

public class ActorIdentityAssertionsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "known")]
    [InlineData("same", "same")]
    public async Task ShouldHaveConsistentActorIdentityAsync_ValidAgreementOrActorAbsence_Succeeds(
        string? actorId, string? identityId)
    {
        var provider = new Provider(actorId, identityId);
        await provider.ShouldHaveConsistentActorIdentityAsync(provider, Token);
        provider.Calls.Should().Equal(["identity", "actor"]);
    }

    [Theory]
    [InlineData("full", null)]
    [InlineData("full", "different")]
    public async Task ShouldHaveConsistentActorIdentityAsync_MissingOrDifferentIdentity_DiagnosesBothProviders(
        string actorId, string? identityId)
    {
        var provider = new Provider(actorId, identityId);
        var act = () => provider.ShouldHaveConsistentActorIdentityAsync(provider, Token);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Provider*Provider*full actor resolved*full*identity-only resolved*");
    }

    [Fact]
    public async Task ShouldHaveConsistentActorIdentityAsync_AssertionScope_RecordsMismatchWithoutUnwrappingNone()
    {
        var provider = new Provider("full", null);
        using var scope = new AssertionScope();
        await provider.ShouldHaveConsistentActorIdentityAsync(provider, Token);

        scope.Discard().Should().ContainSingle().Which.Should().Contain("identity-only resolved").And.Contain("None");
    }

    [Fact]
    public async Task ShouldHaveConsistentActorIdentityAsync_IdentityFailure_PropagatesWithoutResolvingActor()
    {
        var provider = new Provider("full", "full") { IdentityFailure = new InvalidOperationException("lookup failed") };
        var act = () => provider.ShouldHaveConsistentActorIdentityAsync(provider, Token);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(provider.IdentityFailure);
        provider.Calls.Should().Equal(["identity"]);
    }

    [Fact]
    public async Task ShouldHaveConsistentActorIdentityAsync_ActorFailure_Propagates()
    {
        var provider = new Provider("full", "full") { ActorFailure = new InvalidOperationException("actor failed") };
        var act = () => provider.ShouldHaveConsistentActorIdentityAsync(provider, Token);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(provider.ActorFailure);
    }

    [Fact]
    public async Task ShouldHaveConsistentActorIdentityAsync_Cancelled_DoesNotResolveEitherProvider()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var provider = new Provider("full", "full");
        var act = () => provider.ShouldHaveConsistentActorIdentityAsync(provider, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        provider.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCurrentActorIdAsync_TestActorScopes_TracksCanonicalIdentityAndRestoresIt()
    {
        var provider = new TestActorProvider("default");
        using (provider.WithActor(" scoped "))
        {
            (await provider.GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be("scoped");
            await provider.ShouldHaveConsistentActorIdentityAsync(provider, Token);
        }

        (await provider.GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be("default");
    }

    private sealed class Provider(string? actorId, string? identityId) : IActorProvider, IActorIdentityProvider
    {
        public List<string> Calls { get; } = [];
        public Exception? IdentityFailure { get; init; }
        public Exception? ActorFailure { get; init; }

        public ValueTask<Maybe<ActorId>> GetCurrentActorIdAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("identity");
            return IdentityFailure is not null
                ? ValueTask.FromException<Maybe<ActorId>>(IdentityFailure)
                : ValueTask.FromResult(Maybe.From(identityId is null ? null : ActorId.Create(identityId)));
        }

        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)
        {
            Calls.Add("actor");
            return ActorFailure is not null
                ? Task.FromException<Maybe<Actor>>(ActorFailure)
                : Task.FromResult(Maybe.From(actorId is null ? null : Actor.Create(actorId, new HashSet<string>())));
        }
    }
}