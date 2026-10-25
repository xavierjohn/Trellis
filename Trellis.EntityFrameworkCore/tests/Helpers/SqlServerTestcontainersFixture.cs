[assembly: AssemblyFixture(typeof(SqlServerTestcontainersFixture))]

namespace Trellis.EntityFrameworkCore.Tests.Helpers;

using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Trellis.TestSupport;

/// <summary>
/// Owns the optional SQL Server container shared by this test assembly.
/// </summary>
public sealed class SqlServerTestcontainersFixture : IAsyncLifetime
{
    // Pinned by digest so a registry-side change cannot alter the required check. To move to a newer build of the
    // same tag, refresh the digest: docker buildx imagetools inspect <tag> --format '{{.Manifest.Digest}}'
    private const string Image = "mcr.microsoft.com/mssql/server:2025-CU10-ubuntu-24.04@sha256:245f6b1c8903edae52cb20fa31419cefcde1a49938bb8c3447099011fc871ca3";
    private const string OwnerLabel = "trellis.testcontainers-fixture";
    private const string Owner = "sqlserver-specifications";
    private readonly Func<IDatabaseContainer> _createContainer;
    private readonly ContainerLogs _logs = new("sqlserver-integration", "sqlserver");
    private IDatabaseContainer? _container;

    /// <summary>Creates the optional SQL Server fixture.</summary>
    public SqlServerTestcontainersFixture() : this(Environment.GetEnvironmentVariable)
    {
    }

    internal SqlServerTestcontainersFixture(Func<string, string?> getEnvironmentVariable)
    {
        IsEnabled = ReadEnabled(getEnvironmentVariable);
        _createContainer = CreateContainer;
    }

    internal SqlServerTestcontainersFixture(bool enabled, Func<IDatabaseContainer> createContainer)
    {
        IsEnabled = enabled;
        _createContainer = createContainer;
    }

    private static bool ReadEnabled(Func<string, string?> getEnvironmentVariable)
    {
        var enabled = getEnvironmentVariable("TRELLIS_TEST_SQLSERVER") == "testcontainers";
        if (enabled && !string.IsNullOrWhiteSpace(getEnvironmentVariable("TRELLIS_TEST_SQLSERVER_CONNECTION")))
            throw new InvalidOperationException(
                "Do not combine TRELLIS_TEST_SQLSERVER=testcontainers with TRELLIS_TEST_SQLSERVER_CONNECTION.");
        return enabled;
    }

    /// <summary>Gets whether the Testcontainers backend was explicitly selected.</summary>
    public bool IsEnabled { get; }

    /// <summary>Creates a connection string for an isolated test database.</summary>
    public string CreateConnectionString()
    {
        if (_container is null)
            throw new InvalidOperationException("The SQL Server container is not initialized.");

        return new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = $"TrellisSpec_{Guid.NewGuid():N}"
        }.ConnectionString;
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        if (!IsEnabled)
            return;

        _container = _createContainer();
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        startup.CancelAfter(ContainerFixtureSupport.StartupTimeout);
        await _container.StartAsync(startup.Token);
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