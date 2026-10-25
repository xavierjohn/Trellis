namespace Trellis.EntityFrameworkCore.Tests;

using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Moq;
using Trellis.TestSupport;

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
        Func<Task> request = () => fixture.CreateConnectionStringAsync().AsTask();
        await request.Should().ThrowExactlyAsync<InvalidOperationException>();
        factoryCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("true", null, false)]
    [InlineData("true", "Server=.;Database=x", false)]
    [InlineData("testcontainers", null, true)]
    [InlineData("testcontainers", "  ", true)]
    public void Constructor_BackendSelection_ReadsOnlyTheTestcontainersValue(
        string? backend, string? connection, bool expectedEnabled)
    {
        var fixture = new SqlServerTestcontainersFixture(SqlServerEnvironment(backend, connection));

        fixture.IsEnabled.Should().Be(expectedEnabled);
    }

    [Fact]
    public async Task CreateConnectionStringAsync_TestcontainersWithExternalConnection_ThrowsOnDemand()
    {
        await using var fixture = new SqlServerTestcontainersFixture(
            SqlServerEnvironment("testcontainers", "Server=.;Database=x"));

        await fixture.InitializeAsync();
        fixture.IsEnabled.Should().BeTrue();
        Func<Task> request = () => fixture.CreateConnectionStringAsync().AsTask();

        (await request.Should().ThrowExactlyAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("TRELLIS_TEST_SQLSERVER_CONNECTION");
    }

    private static Func<string, string?> SqlServerEnvironment(string? backend, string? connection) =>
        FakeEnvironment.With(
            ("TRELLIS_TEST_SQLSERVER", backend),
            ("TRELLIS_TEST_SQLSERVER_CONNECTION", connection));

    [Fact]
    public async Task CreateConnectionStringAsync_ConcurrentRequests_SharesStartupAndIsolatesDatabases()
    {
        var startup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var container = new Mock<IDatabaseContainer>(MockBehavior.Strict);
        var factoryCalls = 0;
        CancellationToken startupToken = default;
        container.Setup(c => c.StartAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(token => startupToken = token)
            .Returns(startup.Task);
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

            factoryCalls.Should().Be(0);
            container.Verify(c => c.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
            var firstRequest = fixture.CreateConnectionStringAsync().AsTask();
            var secondRequest = fixture.CreateConnectionStringAsync().AsTask();

            container.Verify(c => c.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
            firstRequest.IsCompleted.Should().BeFalse();
            secondRequest.IsCompleted.Should().BeFalse();
            startup.SetResult();
            var first = new SqlConnectionStringBuilder(await firstRequest);
            var second = new SqlConnectionStringBuilder(await secondRequest);
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
            await fixture.InitializeAsync();
            Func<Task> request = () => fixture.CreateConnectionStringAsync().AsTask();
            var thrown = await request.Should().ThrowExactlyAsync<InvalidOperationException>();
            thrown.Which.Should().BeSameAs(failure);
            (await request.Should().ThrowExactlyAsync<InvalidOperationException>())
                .Which.Should().BeSameAs(failure);
            container.Verify(c => c.StartAsync(It.IsAny<CancellationToken>()), Times.Once);

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