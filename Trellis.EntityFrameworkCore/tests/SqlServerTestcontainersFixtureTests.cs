namespace Trellis.EntityFrameworkCore.Tests;

using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Moq;

public class SqlServerTestcontainersFixtureTests
{
    [Fact]
    public async Task InitializeAsync_Disabled_FixtureLifecycle_DoesNotCreateContainer()
    {
        var factoryCalls = 0;
        await using var fixture = new SqlServerTestcontainersFixture(false, () =>
        {
            factoryCalls++;
            throw new InvalidOperationException("Docker must not be accessed.");
        });

        await fixture.InitializeAsync();

        fixture.IsEnabled.Should().BeFalse();
        factoryCalls.Should().Be(0);
    }

    [Fact]
    public async Task InitializeAsync_Enabled_MultipleDatabases_SharesContainerAndIsolatesDatabases()
    {
        var container = new Mock<IDatabaseContainer>(MockBehavior.Strict);
        var factoryCalls = 0;
        CancellationToken startupToken = default;
        container.Setup(c => c.StartAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(token => startupToken = token)
            .Returns(Task.CompletedTask);
        container.Setup(c => c.GetConnectionString())
            .Returns("Server=127.0.0.1,49152;Database=master;Integrated Security=True;TrustServerCertificate=True");
        container.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);

        await using (var fixture = new SqlServerTestcontainersFixture(true, () =>
        {
            factoryCalls++;
            return container.Object;
        }))
        {
            await fixture.InitializeAsync();

            container.Verify(c => c.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
            var first = new SqlConnectionStringBuilder(fixture.CreateConnectionString());
            var second = new SqlConnectionStringBuilder(fixture.CreateConnectionString());

            fixture.IsEnabled.Should().BeTrue();
            factoryCalls.Should().Be(1);
            startupToken.CanBeCanceled.Should().BeTrue();
            first.DataSource.Should().Be("127.0.0.1,49152");
            second.DataSource.Should().Be(first.DataSource);
            first.InitialCatalog.Should().StartWith("TrellisSpec_").And.NotBe("master");
            second.InitialCatalog.Should().StartWith("TrellisSpec_").And.NotBe(first.InitialCatalog);
        }

        container.Verify(c => c.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_FailedStartup_PendingCleanup_AwaitsCleanupAndPreservesFailure()
    {
        var failure = new InvalidOperationException("Container startup failed.");
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var container = new Mock<IDatabaseContainer>(MockBehavior.Strict);
        container.Setup(c => c.StartAsync(It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        container.Setup(c => c.DisposeAsync()).Callback(cleanupEntered.SetResult)
            .Returns(() => new ValueTask(releaseCleanup.Task));
        var fixture = new SqlServerTestcontainersFixture(true, () => container.Object);
        Task? cleanup = null;

        try
        {
            Func<Task> initialize = () => fixture.InitializeAsync().AsTask();
            var thrown = await initialize.Should().ThrowExactlyAsync<InvalidOperationException>();
            thrown.Which.Should().BeSameAs(failure);

            cleanup = fixture.DisposeAsync().AsTask();
            await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            cleanup.IsCompleted.Should().BeFalse();
        }
        finally
        {
            releaseCleanup.TrySetResult();
            if (cleanup is null)
                await fixture.DisposeAsync();
            else
                await cleanup.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }

        container.Verify(c => c.DisposeAsync(), Times.Once);
    }
}