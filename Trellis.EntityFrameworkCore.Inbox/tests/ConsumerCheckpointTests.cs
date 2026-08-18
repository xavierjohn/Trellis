namespace Trellis.EntityFrameworkCore.Inbox.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable CA1707 // readable xUnit test names

public sealed class ConsumerCheckpointTests
{
    [Fact]
    public async Task GetAsync_returns_None_when_the_consumer_has_never_checkpointed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection);
        await EnsureCreatedAsync(provider, ct);
        var store = provider.GetRequiredService<IConsumerCheckpointStore>();

        var position = await store.GetAsync("billing", ct);

        position.HasValue.Should().BeFalse("no checkpoint has been written");
    }

    [Fact]
    public async Task SetAsync_then_GetAsync_returns_the_persisted_position()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection);
        await EnsureCreatedAsync(provider, ct);
        var store = provider.GetRequiredService<IConsumerCheckpointStore>();

        await store.SetAsync("billing", "cursor-1", ct);

        (await store.GetAsync("billing", ct)).GetValueOrDefault("").Should().Be("cursor-1");
    }

    [Fact]
    public async Task SetAsync_advances_an_existing_checkpoint_without_adding_a_row()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection);
        await EnsureCreatedAsync(provider, ct);
        var store = provider.GetRequiredService<IConsumerCheckpointStore>();

        await store.SetAsync("billing", "cursor-1", ct);
        await store.SetAsync("billing", "cursor-2", ct);

        (await store.GetAsync("billing", ct)).GetValueOrDefault("").Should().Be("cursor-2", "the latest write wins");

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
        (await db.Set<ConsumerCheckpoint>().CountAsync(ct)).Should().Be(1, "advancing updates the single row, not inserts");
    }

    [Fact]
    public async Task Checkpoints_are_isolated_per_consumer()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection);
        await EnsureCreatedAsync(provider, ct);
        var store = provider.GetRequiredService<IConsumerCheckpointStore>();

        await store.SetAsync("consumer-a", "a-1", ct);

        (await store.GetAsync("consumer-b", ct)).HasValue.Should().BeFalse("consumer-b has its own cursor");
        (await store.GetAsync("consumer-a", ct)).GetValueOrDefault("").Should().Be("a-1");
    }

    [Fact]
    public async Task SetAsync_persists_durably_so_a_fresh_context_sees_it()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection);
        await EnsureCreatedAsync(provider, ct);

        await provider.GetRequiredService<IConsumerCheckpointStore>().SetAsync("billing", "cursor-1", ct);

        // A different context instance reads the committed row — proving SetAsync saved rather than only staged.
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
        var row = await db.Set<ConsumerCheckpoint>().AsNoTracking().SingleAsync(ct);
        row.Position.Should().Be("cursor-1");
        row.UpdatedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task GetAsync_requires_a_consumerId()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection);
        await EnsureCreatedAsync(provider, ct);
        var store = provider.GetRequiredService<IConsumerCheckpointStore>();

        var act = async () => await store.GetAsync(" ", ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SetAsync_requires_a_consumerId()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection);
        await EnsureCreatedAsync(provider, ct);
        var store = provider.GetRequiredService<IConsumerCheckpointStore>();

        var act = async () => await store.SetAsync(" ", "cursor-1", ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SetAsync_requires_a_position()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection);
        await EnsureCreatedAsync(provider, ct);
        var store = provider.GetRequiredService<IConsumerCheckpointStore>();

        var act = async () => await store.SetAsync("billing", " ", ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetAsync_rejects_a_consumerId_longer_than_the_key_column()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection);
        await EnsureCreatedAsync(provider, ct);
        var store = provider.GetRequiredService<IConsumerCheckpointStore>();
        var tooLong = new string('x', InboxOptions.MaxConsumerIdLength + 1);

        var act = async () => await store.GetAsync(tooLong, ct);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage($"*{InboxOptions.MaxConsumerIdLength}*");
    }

    [Fact]
    public async Task SetAsync_rejects_a_consumerId_longer_than_the_key_column()
    {
        var ct = TestContext.Current.CancellationToken;
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(ct);

        await using var provider = BuildProvider(connection);
        await EnsureCreatedAsync(provider, ct);
        var store = provider.GetRequiredService<IConsumerCheckpointStore>();
        var tooLong = new string('x', InboxOptions.MaxConsumerIdLength + 1);

        var act = async () => await store.SetAsync(tooLong, "cursor-1", ct);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage($"*{InboxOptions.MaxConsumerIdLength}*");
    }

    [Fact]
    public void AddTrellisConsumerCheckpointStore_registers_the_store()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<InboxTestDbContext>(o => o.UseSqlite(connection));
        services.AddTrellisConsumerCheckpointStore<InboxTestDbContext>();

        using var provider = services.BuildServiceProvider();

        provider.GetService<IConsumerCheckpointStore>().Should().NotBeNull();
    }

    private static async Task EnsureCreatedAsync(IServiceProvider provider, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InboxTestDbContext>();
        await db.Database.EnsureCreatedAsync(ct);
    }

    private static ServiceProvider BuildProvider(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<InboxTestDbContext>(o => o.UseSqlite(connection));
        services.AddTrellisConsumerCheckpointStore<InboxTestDbContext>();
        return services.BuildServiceProvider();
    }
}

#pragma warning restore CA1707