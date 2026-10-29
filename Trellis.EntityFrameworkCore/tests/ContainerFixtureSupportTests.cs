namespace Trellis.EntityFrameworkCore.Tests;

using Trellis.TestSupport;

public class ContainerFixtureSupportTests
{
    [Fact]
    public async Task DisposeWithinAsync_CompletedCleanup_DisposesLogs()
    {
        var logs = new RecordingDisposable();

        await ContainerFixtureSupport.DisposeWithinAsync(Task.CompletedTask, logs, TimeSpan.FromSeconds(5));

        logs.Disposals.Should().Be(1);
    }

    [Fact]
    public async Task DisposeWithinAsync_PendingCleanup_TimesOutWithoutDisposingLogs()
    {
        var logs = new RecordingDisposable();
        var neverCompletes = new TaskCompletionSource();

        var dispose = async () => await ContainerFixtureSupport.DisposeWithinAsync(
            neverCompletes.Task, logs, TimeSpan.FromMilliseconds(50));

        await dispose.Should().ThrowExactlyAsync<TimeoutException>();
        logs.Disposals.Should().Be(0);
        neverCompletes.SetResult();
    }

    [Fact]
    public async Task DisposeWithinAsync_FailedCleanup_PropagatesFailureAndStillDisposesLogs()
    {
        var logs = new RecordingDisposable();
        var failure = new InvalidOperationException("cleanup failed");

        var dispose = async () => await ContainerFixtureSupport.DisposeWithinAsync(
            Task.FromException(failure), logs, TimeSpan.FromSeconds(5));

        (await dispose.Should().ThrowExactlyAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        logs.Disposals.Should().Be(1);
    }

    [Fact]
    public void ContainerLogs_Create_WritesUnderTestResultsAndReleasesFilesOnDispose()
    {
        var directoryName = $"container-support-{Guid.NewGuid():N}";
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults", directoryName);

        try
        {
            var logs = new ContainerLogs(directoryName, "unit");
            _ = logs.Create("first");
            _ = logs.Create("second");

            var files = Directory.GetFiles(directory, "unit-*.log");
            files.Should().HaveCount(2);
            files.Select(Path.GetFileName).Should().Contain(name => name!.EndsWith("-first.log"))
                .And.Contain(name => name!.EndsWith("-second.log"));

            logs.Dispose();

            foreach (var file in files)
                File.Delete(file);
            Directory.GetFiles(directory).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void NewSqlPassword_MeetsSqlServerComplexityAndIsUnique()
    {
        var first = ContainerFixtureSupport.NewSqlPassword();
        var second = ContainerFixtureSupport.NewSqlPassword();

        first.Should().NotBe(second);
        first.Length.Should().BeGreaterThanOrEqualTo(8);
        first.Should().MatchRegex("[a-z]").And.MatchRegex("[A-Z]").And.MatchRegex("[0-9]").And.MatchRegex("[^a-zA-Z0-9]");
    }

    private sealed class RecordingDisposable : IDisposable
    {
        public int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }
}