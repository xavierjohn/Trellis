namespace Trellis.Core.Tests.Errors;

using System.Diagnostics;
using System.Xml.Linq;

public class ClosedHierarchyCompilationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchExpression_ShippedHierarchies_CaseCoverage_IsCompilerChecked(bool omitLastCase)
    {
        var directory = Directory.CreateTempSubdirectory("trellis-closed-hierarchies-").FullName;
        try
        {
            var project = new XDocument(
                new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                    new XElement("PropertyGroup",
                        new XElement("TargetFramework", "net11.0"),
                        new XElement("LangVersion", "15.0"),
                        new XElement("TreatWarningsAsErrors", "true"),
                        new XElement("DefineConstants", omitLastCase ? "OMIT_LAST_CASE" : string.Empty)),
                    new XElement("ItemGroup",
                        new XElement("Reference", new XAttribute("Include", "Trellis.Core"),
                            new XElement("HintPath", typeof(Error).Assembly.Location)),
                        new XElement("Reference", new XAttribute("Include", "Trellis.Http.Abstractions"),
                            new XElement("HintPath", typeof(HttpError).Assembly.Location)))));
            var projectPath = Path.Combine(directory, "Consumer.csproj");
            await File.WriteAllTextAsync(projectPath, project.ToString(), TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "Consumer.cs"), ConsumerSource,
                TestContext.Current.CancellationToken);

            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in new[] { "build", projectPath, "--disable-build-servers", "--verbosity", "quiet" })
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the closed-hierarchy consumer build.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                var output = await stdout + await stderr;
                if (omitLastCase)
                {
                    process.ExitCode.Should().NotBe(0, output);
                    output.Should().Contain("error CS8509");
                }
                else
                    process.ExitCode.Should().Be(0, output);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private const string ConsumerSource = """
        using Trellis;

        public static class Consumer
        {
            public static int Describe(Error error) => error switch
            {
                Error.InvalidInput => 1,
                Error.InvariantViolation => 2,
                Error.NotFound => 3,
                Error.Forbidden => 4,
                Error.Conflict => 5,
                Error.Gone => 6,
                Error.AuthenticationRequired => 7,
                Error.Unavailable => 8,
                Error.RateLimited => 9,
                Error.Unexpected => 10,
                Error.Aggregate => 11,
        #if !OMIT_LAST_CASE
                Error.TransportFault => 12,
        #endif
            };

            public static int Describe(HttpError error) => error switch
            {
                HttpError.MethodNotAllowed => 1,
                HttpError.NotAcceptable => 2,
                HttpError.UnsupportedMediaType => 3,
                HttpError.RangeNotSatisfiable => 4,
                HttpError.ContentTooLarge => 5,
                HttpError.PreconditionFailed => 6,
        #if !OMIT_LAST_CASE
                HttpError.PreconditionRequired => 7,
        #endif
            };

            public static int Describe(WriteOutcome<int> outcome) => outcome switch
            {
                WriteOutcome<int>.Created => 1,
                WriteOutcome<int>.Updated => 2,
                WriteOutcome<int>.UpdatedNoContent => 3,
                WriteOutcome<int>.Accepted => 4,
        #if !OMIT_LAST_CASE
                WriteOutcome<int>.AcceptedNoContent => 5,
        #endif
            };
        }
        """;
}