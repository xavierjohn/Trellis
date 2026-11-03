namespace Trellis.Testing.Tests.Fakes;

using Trellis.Authorization;

public class AnonymousActorTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anonymous_Provider_ResolvesNoActorAndNoIdentity()
    {
        var provider = TestActorProvider.Anonymous();

        (await provider.GetCurrentActorAsync(Token)).Should().BeNone();
        (await provider.GetCurrentActorIdAsync(Token)).Should().BeNone();
    }

    [Fact]
    public async Task Anonymous_Provider_WithActorScope_ResolvesThenRestoresAnonymous()
    {
        var provider = TestActorProvider.Anonymous();

        using (provider.WithActor("alice", "Orders.Read"))
        {
            (await provider.GetCurrentActorAsync(Token)).Unwrap().Id.Value.Should().Be("alice");
            (await provider.GetCurrentActorIdAsync(Token)).Unwrap().Value.Should().Be("alice");
        }

        (await provider.GetCurrentActorAsync(Token)).Should().BeNone();
    }

    [Fact]
    public async Task WithoutActor_Scope_MakesAuthenticatedProviderAnonymousThenRestores()
    {
        var provider = new TestActorProvider("alice", "Orders.Read");

        await using (provider.WithoutActor())
        {
            (await provider.GetCurrentActorAsync(Token)).Should().BeNone();
            (await provider.GetCurrentActorIdAsync(Token)).Should().BeNone();
        }

        (await provider.GetCurrentActorAsync(Token)).Unwrap().Id.Value.Should().Be("alice");
    }

    [Fact]
    public async Task WithoutActor_NestedInsideWithActor_RestoresPreviousScopeOnDispose()
    {
        var provider = new TestActorProvider("alice");

        using (provider.WithActor("bob"))
        {
            using (provider.WithoutActor())
                (await provider.GetCurrentActorAsync(Token)).Should().BeNone();

            (await provider.GetCurrentActorAsync(Token)).Unwrap().Id.Value.Should().Be("bob");
        }
    }

    [Fact]
    public async Task WithoutActor_Identity_HonorsCancellation()
    {
        var provider = TestActorProvider.Anonymous();
        using var canceled = CanceledToken.Create();

        var act = async () => await provider.GetCurrentActorIdAsync(canceled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WithoutActor_ScopesAreIsolatedAcrossParallelFlows()
    {
        var provider = new TestActorProvider("alice");

        var anonymous = Task.Run(async () =>
        {
            using var scope = provider.WithoutActor();
            await Task.Yield();
            return await provider.GetCurrentActorAsync(Token);
        }, Token);
        var authenticated = Task.Run(async () =>
        {
            await Task.Yield();
            return await provider.GetCurrentActorAsync(Token);
        }, Token);

        (await anonymous).Should().BeNone();
        (await authenticated).Unwrap().Id.Value.Should().Be("alice");
    }
}