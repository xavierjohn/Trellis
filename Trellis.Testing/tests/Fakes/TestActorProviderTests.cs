namespace Trellis.Testing.Tests.Fakes;

using Trellis.Authorization;
using Trellis.Testing.Fakes;

/// <summary>
/// Tests for <see cref="TestActorProvider"/> and <see cref="TestActorScope"/>.
/// </summary>
public class TestActorProviderTests
{
    #region Constructor Tests

    [Fact]
    public async Task Constructor_WithActor_SetsCurrentActor()
    {
        var actor = Actor.Create("user-1", new HashSet<string>(["Read"]));

        var provider = new TestActorProvider(actor);

        (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Should().BeSameAs(actor);
    }

    [Fact]
    public async Task Constructor_WithUserIdAndPermissions_CreatesActor()
    {
        var provider = new TestActorProvider("user-1", "Read", "Write");

        var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);
        actor.Id.Should().Be("user-1");
        actor.HasPermission("Read").Should().BeTrue();
        actor.HasPermission("Write").Should().BeTrue();
    }

    [Fact]
    public async Task Constructor_WithNoPermissions_CreatesActorWithEmptyPermissions()
    {
        var provider = new TestActorProvider("user-1");

        var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);
        actor.Id.Should().Be("user-1");
        actor.HasPermission("Read").Should().BeFalse();
    }

    #endregion

    #region WithActor Scope Tests

    [Fact]
    public async Task WithActor_Actor_ChangesCurrentActor()
    {
        var original = Actor.Create("admin", new HashSet<string>(["Admin"]));
        var scoped = Actor.Create("user-1", new HashSet<string>(["Read"]));
        var provider = new TestActorProvider(original);

        await using (provider.WithActor(scoped))
        {
            (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Should().BeSameAs(scoped);
        }
    }

    [Fact]
    public async Task WithActor_Actor_RestoresOriginalOnDispose()
    {
        var original = Actor.Create("admin", new HashSet<string>(["Admin"]));
        var scoped = Actor.Create("user-1", new HashSet<string>(["Read"]));
        var provider = new TestActorProvider(original);

        await using (provider.WithActor(scoped))
        {
            // Actor changed inside scope
        }

        (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Should().BeSameAs(original);
    }

    [Fact]
    public async Task WithActor_UserIdAndPermissions_ChangesCurrentActor()
    {
        var provider = new TestActorProvider("admin", "Admin");

        await using (provider.WithActor("user-1", "Read"))
        {
            var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);
            actor.Id.Should().Be("user-1");
            actor.HasPermission("Read").Should().BeTrue();
            actor.HasPermission("Admin").Should().BeFalse();
        }
    }

    [Fact]
    public async Task WithActor_UserIdAndPermissions_RestoresOriginalOnDispose()
    {
        var provider = new TestActorProvider("admin", "Admin");

        await using (provider.WithActor("user-1", "Read"))
        {
            // Actor changed inside scope
        }

        var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);
        actor.Id.Should().Be("admin");
        actor.HasPermission("Admin").Should().BeTrue();
    }

    [Fact]
    public async Task WithActor_NestedScopes_RestoresCorrectActorAtEachLevel()
    {
        var provider = new TestActorProvider("admin", "Admin");

        await using (provider.WithActor("user-1", "Read"))
        {
            (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Id.Should().Be("user-1");

            await using (provider.WithActor("user-2", "Write"))
            {
                (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Id.Should().Be("user-2");
            }

            (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Id.Should().Be("user-1");
        }

        (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Id.Should().Be("admin");
    }

    [Fact]
    public async Task WithActor_SynchronousDispose_RestoresOriginalActor()
    {
        var original = Actor.Create("admin", new HashSet<string>(["Admin"]));
        var provider = new TestActorProvider(original);

        using (provider.WithActor("user-1", "Read"))
        {
            (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Id.Should().Be("user-1");
        }

        (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Should().BeSameAs(original);
    }

    [Fact]
    public async Task WithActor_DoubleDispose_IsIdempotent()
    {
        var original = Actor.Create("admin", new HashSet<string>(["Admin"]));
        var provider = new TestActorProvider(original);

        var scope = provider.WithActor("user-1", "Read");
        await scope.DisposeAsync();

        // Change actor after first dispose
        using (provider.WithActor("user-2", "Write"))
        {
            // Second dispose should be a no-op, not restore to "admin" again
            await scope.DisposeAsync();
            (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Id.Should().Be("user-2");
        }
    }

    #endregion

    #region IActorProvider Contract Tests

    [Fact]
    public async Task GetCurrentActorAsync_ImplementsIActorProvider()
    {
        var provider = new TestActorProvider("user-1", "Read");

        var actor = await ((IActorProvider)provider).GetCurrentActorAsync(TestContext.Current.CancellationToken);

        actor.Id.Should().Be("user-1");
    }

    [Fact]
    public async Task GetCurrentActorAsync_ReflectsScopedActor()
    {
        var provider = new TestActorProvider("admin", "Admin");

        await using (provider.WithActor("user-1", "Read"))
        {
            var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);
            actor.Id.Should().Be("user-1");
            actor.HasPermission("Read").Should().BeTrue();
            actor.HasPermission("Admin").Should().BeFalse();
        }

        var restoredActor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);
        restoredActor.Id.Should().Be("admin");
    }

    #endregion

    #region Parallel Isolation Tests

    [Fact]
    public async Task WithActor_ParallelScopes_DoNotInterfere()
    {
        var provider = new TestActorProvider("admin", "Admin");
        var ct = TestContext.Current.CancellationToken;

        var ready = new TaskCompletionSource();
        var hold = new TaskCompletionSource();

        var task1 = Task.Run(async () =>
        {
            await using (provider.WithActor("user-1", "Read"))
            {
                ready.SetResult();
                await hold.Task;
                // Should still see user-1 even though task2 changed the actor in its flow
                (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Id.Should().Be("user-1");
            }

            // After dispose, this flow falls back to the default actor
            (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Id.Should().Be("admin");
        }, ct);

        var task2 = Task.Run(async () =>
        {
            await ready.Task; // Ensure task1 scope is active
            await using (provider.WithActor("user-2", "Write"))
            {
                (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Id.Should().Be("user-2");
            }

            // After dispose, this flow also falls back to the default actor
            (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Id.Should().Be("admin");

            hold.SetResult(); // Let task1 verify
        }, ct);

        await Task.WhenAll(task1, task2);

        // Default actor is restored outside any scope
        (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Id.Should().Be("admin");
    }

    [Fact]
    public async Task WithActor_HighConcurrency_EachFlowSeesOwnActor()
    {
        var provider = new TestActorProvider("admin", "Admin");
        var ct = TestContext.Current.CancellationToken;
        const int taskCount = 20;
        var barrier = new Barrier(taskCount);

        var tasks = Enumerable.Range(0, taskCount).Select(i => Task.Run(async () =>
        {
            var userId = $"user-{i}";
            var permission = $"Perm-{i}";

            await using (provider.WithActor(userId, permission))
            {
                // Synchronize so all scopes overlap
                barrier.SignalAndWait(ct);

                var actor = await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken);
                actor.Id.Should().Be(userId);
                actor.HasPermission(permission).Should().BeTrue();
            }

            // After dispose, default is visible again in this flow
            (await provider.GetCurrentActorAsync(TestContext.Current.CancellationToken)).Id.Should().Be("admin");
        }, ct));

        await Task.WhenAll(tasks);
    }

    #endregion
}