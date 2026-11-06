namespace Trellis.Core.Tests.Results.Extensions;

/// <summary>
/// Every span the ROP and value-object operators start must be held by a <c>using var activity</c> declaration, so it
/// is stopped on every return and exception path and cannot stay current and become the parent of the next step.
/// </summary>
/// <remarks>
/// The behavioural tests (<see cref="ResultTracingParentageTests"/>, <see cref="ResultTracingSiblingContractTests"/>) prove this
/// for the operators they exercise; this guard covers every start site, including overloads no test calls. The Outbox and
/// Inbox spans are deliberately excluded: they carry explicit lifetimes that span callbacks.
/// </remarks>
public class TraceSpanScopeGuardTests
{
    private static readonly string[] s_guardedRoots =
    [
        "Trellis.Core/src",
        "Trellis.Core/generator",
        "Trellis.Primitives/src",
        "Trellis.Mediator/src",
    ];

    [Fact]
    public void Every_span_started_by_the_operators_is_held_by_a_using_declaration()
    {
        var root = RepositoryRoot();
        var files = new List<string>();
        foreach (var guarded in s_guardedRoots)
            files.AddRange(SourceFiles(Path.Combine(root, guarded)));

        files.Should().NotBeEmpty();
        var offenders = new List<string>();
        foreach (var file in files)
            foreach (var line in UnscopedStarts(File.ReadAllText(file)))
                offenders.Add($"{Path.GetRelativePath(root, file)}: {line}");

        offenders.Should().BeEmpty("a span that is not held by 'using var activity' leaks into the next step");
    }

    [Fact]
    public void The_guarded_roots_contain_start_sites()
    {
        var root = RepositoryRoot();
        var count = 0;
        foreach (var guarded in s_guardedRoots)
            foreach (var file in SourceFiles(Path.Combine(root, guarded)))
                count += File.ReadAllLines(file).Count(line => line.Contains(".StartActivity(", StringComparison.Ordinal));

        count.Should().BeGreaterThan(100, "the guard must see the operator start sites, or it passes vacuously");
    }

    #region The scanner

    [Fact]
    public void Scanner_accepts_a_using_declaration() =>
        UnscopedStarts("    using var activity = RopTrace.ActivitySource.StartActivity(nameof(Map));").Should().BeEmpty();

    [Theory]
    [InlineData("    var activity = RopTrace.ActivitySource.StartActivity(nameof(Map));")]
    [InlineData("    activity = RopTrace.ActivitySource.StartActivity(nameof(Map));")]
    [InlineData("    RopTrace.ActivitySource.StartActivity(nameof(Map));")]
    [InlineData("    using var other = RopTrace.ActivitySource.StartActivity(nameof(Map));")]
    [InlineData("    return RopTrace.ActivitySource.StartActivity(nameof(Map));")]
    public void Scanner_flags_a_start_that_is_not_a_using_declaration(string line) =>
        UnscopedStarts(line).Should().ContainSingle();

    [Theory]
    [InlineData("    /// using var activity = RopTrace.ActivitySource.StartActivity(nameof(Map));")]
    [InlineData("    // var activity = source.StartActivity(nameof(Map));")]
    [InlineData("    var text = nothing.Here();")]
    public void Scanner_ignores_comments_and_unrelated_lines(string line) =>
        UnscopedStarts(line).Should().BeEmpty();

    [Fact]
    public void Scanner_reports_every_offending_line()
    {
        const string text = """
            using var activity = A.StartActivity("a");
            var leaked = B.StartActivity("b");
            using var activity = C.StartActivity("c");
            var alsoLeaked = D.StartActivity("d");
            """;

        UnscopedStarts(text).Should().HaveCount(2);
    }

    private static List<string> UnscopedStarts(string text)
    {
        var offenders = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("//", StringComparison.Ordinal) || !line.Contains(".StartActivity(", StringComparison.Ordinal))
                continue;

            if (!line.StartsWith("using var activity = ", StringComparison.Ordinal))
                offenders.Add(line);
        }

        return offenders;
    }

    #endregion

    private static IEnumerable<string> SourceFiles(string directory)
    {
        Directory.Exists(directory).Should().BeTrue($"{directory} must exist");
        foreach (var extension in new[] { "*.cs", "*.tt" })
        {
            foreach (var file in Directory.EnumerateFiles(directory, extension, SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (normalized.Contains("/obj/", StringComparison.Ordinal)
                    || normalized.Contains("/bin/", StringComparison.Ordinal)
                    || normalized.EndsWith(".g.cs", StringComparison.Ordinal))
                    continue;

                yield return file;
            }
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Trellis.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the test must be able to locate the repository root (Trellis.slnx)");
        return directory!.FullName;
    }
}
