namespace Trellis.TestSupport;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;

// Linked into every test project that owns Testcontainers fixtures; keep it free of project-specific types.
// Its unit tests live in Trellis.EntityFrameworkCore/tests/ContainerFixtureSupportTests.cs.
internal static class ContainerFixtureSupport
{
    internal static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan TeardownTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A throwaway SQL Server SA password that satisfies the server's complexity policy.</summary>
    internal static string NewSqlPassword() => $"{Guid.NewGuid():N}aA1!";

    /// <summary>
    /// Waits for <paramref name="cleanup"/> for at most <paramref name="timeout"/>, then releases the log files
    /// once the teardown has finished, successfully or not, because only an in-flight teardown still owns the output streams.
    /// </summary>
    internal static async ValueTask DisposeWithinAsync(Task cleanup, IDisposable logs, TimeSpan timeout)
    {
        try
        {
            await cleanup.WaitAsync(timeout, CancellationToken.None);
        }
        finally
        {
            if (cleanup.IsCompleted)
                logs.Dispose();
        }
    }
}

/// <summary>Creates per-resource container log files under the test output's <c>TestResults</c> directory.</summary>
internal sealed class ContainerLogs(string directoryName, string prefix) : IDisposable
{
    private readonly List<IOutputConsumer> _outputs = [];
    private readonly string _instance = Guid.NewGuid().ToString("N");

    internal IOutputConsumer Create(string resource)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults", directoryName);
        Directory.CreateDirectory(directory);
        var stream = Stream.Synchronized(File.Create(Path.Combine(directory, $"{prefix}-{_instance}-{resource}.log")));
        var output = Consume.RedirectStdoutAndStderrToStream(stream, stream);
        _outputs.Add(output);
        return output;
    }

    public void Dispose()
    {
        foreach (var output in _outputs)
            output.Dispose();
    }
}
