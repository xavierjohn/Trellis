[assembly: AssemblyFixture(typeof(SqlServerTestcontainersFixture))]

namespace Trellis.EntityFrameworkCore.Tests.Helpers;

using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Trellis.TestSupport;

/// <summary>
/// Owns the optional, lazily started SQL Server container shared by this test assembly.
/// </summary>
public sealed class SqlServerTestcontainersFixture : IAsyncLifetime
{
    // Pinned by digest so a registry-side change cannot alter the required check. To move to a newer build of the
    // same tag, refresh the digest: docker buildx imagetools inspect <tag> --format '{{.Manifest.Digest}}'
    private const string Image = "mcr.microsoft.com/mssql/server:2025-CU10-ubuntu-24.04@sha256:245f6b1c8903edae52cb20fa31419cefcde1a49938bb8c3447099011fc871ca3";
    private const string OwnerLabel = "trellis.testcontainers-fixture";
    private const string Owner = "sqlserver-specifications";
    private readonly Func<IDatabaseContainer> _createContainer;
    private readonly string? _externalConnectionString;
    private readonly Lazy<Task<IDatabaseContainer>> _startup;
    private readonly ContainerLogs _logs = new("sqlserver-integration", "sqlserver");
    private CancellationToken _assemblyCancellationToken;
    private IDatabaseContainer? _container;

    /// <summary>Creates the optional SQL Server fixture.</summary>
    public SqlServerTestcontainersFixture() : this(Environment.GetEnvironmentVariable)
    {
    }

    internal SqlServerTestcontainersFixture(Func<string, string?> getEnvironmentVariable)
    {
        IsEnabled = getEnvironmentVariable("TRELLIS_TEST_SQLSERVER") == "testcontainers";
        _externalConnectionString = IsEnabled ? getEnvironmentVariable("TRELLIS_TEST_SQLSERVER_CONNECTION") : null;
        _createContainer = CreateContainer;
        _startup = new(StartContainerAsync);
    }

    internal SqlServerTestcontainersFixture(bool enabled, Func<IDatabaseContainer> createContainer)
    {
        IsEnabled = enabled;
        _createContainer = createContainer;
        _startup = new(StartContainerAsync);
    }

    /// <summary>Gets whether the Testcontainers backend was explicitly selected.</summary>
    public bool IsEnabled { get; }

    /// <summary>Starts the shared container on demand and creates a connection string for an isolated test database.</summary>
    public async ValueTask<string> CreateConnectionStringAsync()
    {
        if (!IsEnabled)
            throw new InvalidOperationException("SQL Server Testcontainers is not enabled for this test run.");

        var container = await _startup.Value;
        return new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = $"TrellisSpec_{Guid.NewGuid():N}"
        }.ConnectionString;
    }

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        _assemblyCancellationToken = TestContext.Current.CancellationToken;
        return ValueTask.CompletedTask;
    }

    private async Task<IDatabaseContainer> StartContainerAsync()
    {
        if (!string.IsNullOrWhiteSpace(_externalConnectionString))
            throw new InvalidOperationException(
                "Do not combine TRELLIS_TEST_SQLSERVER=testcontainers with TRELLIS_TEST_SQLSERVER_CONNECTION.");

        _container = _createContainer();
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(_assemblyCancellationToken);
        startup.CancelAfter(ContainerFixtureSupport.StartupTimeout);
        await _container.StartAsync(startup.Token);
        return _container;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ContainerFixtureSupport.DisposeWithinAsync(
        _container?.DisposeAsync().AsTask() ?? Task.CompletedTask, _logs, ContainerFixtureSupport.TeardownTimeout);

    private MsSqlContainer CreateContainer() =>
        new MsSqlBuilder(Image)
            .WithPassword(ContainerFixtureSupport.NewSqlPassword())
            .WithLabel(OwnerLabel, Owner)
            .WithOutputConsumer(_logs.Create("sql"))
            .Build();
}