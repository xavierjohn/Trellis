namespace Trellis.ServiceDefaults.Tests;

using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;

public class ActorIdentityBuilderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UseCachingActorProvider_DualContract_IdentitySharesSelectedDecorator(bool worker)
    {
        var services = new ServiceCollection();
        services.AddTrellis(builder =>
        {
            builder.UseCachingActorProvider<DualProvider>();
            if (worker)
                builder.UseWorkerActor(Actor.Create("worker", new HashSet<string>()));
        });
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = root.CreateScope();
        var actor = scope.ServiceProvider.GetRequiredService<IActorProvider>();
        var identity = scope.ServiceProvider.GetRequiredService<IActorIdentityProvider>();

        identity.Should().BeSameAs(actor);
        var id = (await identity.GetCurrentActorIdAsync(TestContext.Current.CancellationToken)).GetValueOrThrow();
        id.Value.Should().Be(worker ? "worker" : "user");
        (await actor.GetCurrentActorAsync(TestContext.Current.CancellationToken)).GetValueOrThrow().Id.Should().Be(id);
    }

    private sealed class DualProvider : IActorProvider, IActorIdentityProvider
    {
        private static readonly Actor s_actor = Actor.Create("user", new HashSet<string>());

        public ValueTask<Maybe<ActorId>> GetCurrentActorIdAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Maybe.From(s_actor.Id));

        public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Maybe.From(s_actor));
    }
}