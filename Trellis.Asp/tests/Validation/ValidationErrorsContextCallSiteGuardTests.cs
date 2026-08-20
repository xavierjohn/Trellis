namespace Trellis.Asp.Tests.Validation;

using System.Text.RegularExpressions;

/// <summary>
/// Structural guard: the framework's own body producers must call <c>AddBodyError</c>, never
/// <c>AddError</c>.
///
/// The distinction is load-bearing. <c>AddBodyError</c> promotes an unlocated violation to
/// <see cref="InputLocation.Body"/>; <c>AddError</c> deliberately does not, because it is
/// public and an application filter may legitimately call it to report a query parameter.
/// Promoting there would stamp <c>body</c> on a violation that is nothing of the sort.
///
/// That makes the fence structural rather than conventional — and this test is what keeps it
/// standing. "Every <c>AddError</c> caller is a JSON converter" is true of the framework today
/// and unenforceable by inspection tomorrow; "no production <c>AddError</c> call sites remain"
/// is checkable, and this checks it.
/// </summary>
public class ValidationErrorsContextCallSiteGuardTests
{
    /// <summary>
    /// The sole file allowed to mention <c>AddError</c>: the one that declares it.
    /// </summary>
    private const string DeclaringFile = "ValidationErrorsContext.cs";

    [Fact]
    public void No_production_code_calls_AddError()
    {
        var offenders = ProductionSources()
            .Where(file => !string.Equals(Path.GetFileName(file), DeclaringFile, StringComparison.Ordinal))
            .Select(file => (File: file, Hits: CountCalls(File.ReadAllText(file), "AddError")))
            .Where(x => x.Hits > 0)
            .Select(x => $"{Relative(x.File)} ({x.Hits})")
            .ToArray();

        offenders.Should().BeEmpty(
            "framework body producers must call AddBodyError so the body location is asserted "
            + "in the signature rather than assumed from the caller");
    }

    /// <summary>
    /// Pins the migration total, so a newly added producer that quietly reintroduces the old
    /// shape is visible as a count change rather than passing unnoticed.
    /// </summary>
    [Fact]
    public void All_sixteen_framework_producers_call_AddBodyError()
    {
        var total = ProductionSources()
            .Where(file => !string.Equals(Path.GetFileName(file), DeclaringFile, StringComparison.Ordinal))
            .Sum(file => CountCalls(File.ReadAllText(file), "AddBodyError"));

        total.Should().Be(16);
    }

    /// <summary>
    /// Counts real invocations, ignoring XML doc references such as
    /// <c>&lt;see cref="AddError(string, string)"/&gt;</c>, which name the member without calling it.
    /// </summary>
    private static int CountCalls(string source, string methodName) =>
        Regex.Count(source, @"ValidationErrorsContext\." + methodName + @"\(");

    private static IEnumerable<string> ProductionSources()
    {
        var root = RepositoryRoot();
        foreach (var area in new[] { "Trellis.Asp", "Trellis.Core", "Trellis.Primitives" })
        {
            foreach (var subdirectory in new[] { "src", "generator" })
            {
                var path = Path.Combine(root, area, subdirectory);
                if (!Directory.Exists(path))
                    continue;

                foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
                {
                    if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    yield return file;
                }
            }
        }
    }

    private static string Relative(string file) =>
        Path.GetRelativePath(RepositoryRoot(), file);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Trellis.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the test must be able to locate the repository root (Trellis.slnx)");
        return directory!.FullName;
    }
}
