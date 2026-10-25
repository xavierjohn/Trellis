namespace Trellis.EntityFrameworkCore.Tests.Helpers;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

/// <summary>
/// Owns the optional SQL Server container shared by the specification tests.
/// </summary>
public sealed class SqlServerTestcontainersFixture : IAsyncLifetime
{
    private const string Image = "mcr.microsoft.com/mssql/server:2025-CU10-ubuntu-24.04@sha256:245f6b1c8903edae52cb20fa31419cefcde1a49938bb8c3447099011fc871ca3";
    private const string OwnerLabel = "trellis.testcontainers-fixture";
    private const string Owner = "sqlserver-specifications";
    private readonly Func<IDatabaseContainer> _createContainer;
    private IDatabaseContainer? _container;
    private IOutputConsumer? _outputConsumer;

    /// <summary>Creates the optional SQL Server fixture.</summary>
    public SqlServerTestcontainersFixture()
    {
        IsEnabled = ReadEnabled();
        _createContainer = CreateContainer;
    }

    internal SqlServerTestcontainersFixture(bool enabled, Func<IDatabaseContainer> createContainer)
    {
        IsEnabled = enabled;
        _createContainer = createContainer;
    }

    private static bool ReadEnabled()
    {
        var enabled = Environment.GetEnvironmentVariable("TRELLIS_TEST_SQLSERVER") == "testcontainers";
        if (enabled && !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("TRELLIS_TEST_SQLSERVER_CONNECTION")))
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
        startup.CancelAfter(TimeSpan.FromMinutes(5));
        await _container.StartAsync(startup.Token);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var cleanup = _container?.DisposeAsync().AsTask();
        try
        {
            if (cleanup is not null)
                await cleanup.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        }
        finally
        {
            // An in-flight teardown still owns the output streams.
            if (cleanup is null || cleanup.IsCompletedSuccessfully)
                _outputConsumer?.Dispose();
        }
    }

    private MsSqlContainer CreateContainer()
    {
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults", "sqlserver-integration");
        Directory.CreateDirectory(logDirectory);
        var output = Stream.Synchronized(File.Create(Path.Combine(logDirectory, $"sqlserver-{Guid.NewGuid():N}.log")));
        _outputConsumer = Consume.RedirectStdoutAndStderrToStream(output, output);
        return new MsSqlBuilder(Image)
            .WithPassword($"{Guid.NewGuid():N}aA1!")
            .WithLabel(OwnerLabel, Owner)
            .WithOutputConsumer(_outputConsumer)
            .Build();
    }
}