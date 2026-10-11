namespace Trellis.Analyzers.Tests;

using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

public class ReasonCodeGuardBuildTests
{
    [Theory]
    [InlineData("new Error.Unexpected(Guid.NewGuid().ToString(\"N\"))", true)]
    [InlineData("new(Guid.NewGuid().ToString(\"N\"))", true)]
    [InlineData("new(Code: Guid.CreateVersion7().ToString(\"N\"))", true)]
    [InlineData("new(\n Code: Guid.NewGuid().ToString(\"N\"),\n FaultId: \"incident\")", true)]
    [InlineData("new($\"fault-{Guid.NewGuid():N}\")", true)]
    [InlineData("new(string.Concat(\"fault-\", \"a\", \"b\", \"c\", Guid.NewGuid().ToString(\"N\")))", true)]
    [InlineData("new(string.Concat(\"fault-\", \"a\", \"b\", Guid.NewGuid()))", true)]
    [InlineData("new(string.Concat((ReadOnlySpan<string?>)[\"fault-\", Guid.NewGuid().ToString(\"N\")]))", true)]
    [InlineData("new(string.Concat((ReadOnlySpan<object?>)[\"fault-\", Guid.NewGuid()]))", true)]
    [InlineData("new(string.Concat((ReadOnlySpan<string?>)[.. new[] { \"fault-\", Guid.NewGuid().ToString(\"N\") }]))", true)]
    [InlineData("new(Convert.ToString(new[] { Guid.NewGuid() })!)", false)]
    [InlineData("new(string.Concat((object)new[] { Guid.NewGuid() }))", false)]
    [InlineData("new(string.Concat(new object[] { new[] { Guid.NewGuid() } }))", false)]
    [InlineData("new(string.Concat((ReadOnlySpan<object?>)[.. new object[] { new[] { Guid.NewGuid() } }]))", false)]
    [InlineData("new(Code: \"order.failed\", FaultId: Guid.NewGuid().ToString(\"N\"))", false)]
    [InlineData("new(Code: \"order.\" + nameof(Probe), FaultId: Guid.NewGuid().ToString(\"N\"))", false)]
    public async Task Build_EnabledGuard_OnlyFreshReasonCodeFails(string expression, bool fails)
    {
        var result = await BuildAsync($$"""
            Error.Unexpected error = {{expression}};
            CustomerId id = new(Guid.NewGuid().ToString("N"));
            _ = default(Result<int>);
            """);

        result.ExitCode.Should().Be(fails ? 1 : 0, result.Output);
        if (fails)
            result.Output.Should().Contain("error TRLS067");
        else
            result.Output.Should().NotContain("TRLS067");

        result.Output.Should().NotContain("TRLS019");
    }

    [Fact]
    public async Task Build_EnabledGuard_SingleWriteLocalReasonFails()
    {
        var result = await BuildAsync("""
            var incident = Guid.NewGuid();
            var code = incident.ToString("N");
            Error.Unexpected error = new(Code: code);
            """);

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("error TRLS067");
    }

    [Fact]
    public async Task Build_EnabledGuard_CollectionSpread_MutableLocalRemainsUnknown()
    {
        var result = await BuildAsync("""
            var parts = new[] { Guid.NewGuid().ToString("N") };
            parts[0] = "order.failed";
            Error.Unexpected error = new(string.Concat((ReadOnlySpan<string?>)[.. parts]));
            """);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().NotContain("TRLS067");
    }

    [Theory]
    [InlineData("new(\"order.failed\")", 0)]
    [InlineData("new(Guid.NewGuid().ToString(\"N\"))", 1)]
    public async Task Build_AlreadyLoadedAnalyzers_PreservesOtherRulesAndEnforcesGuard(string expression, int exitCode)
    {
        var result = await BuildAsync($$"""
            _ = default(Result<int>);
            Error.Unexpected error = {{expression}};
            """, alreadyLoaded: true);

        result.ExitCode.Should().Be(exitCode, result.Output);
        result.Output.Should().Contain("warning TRLS019");
        if (exitCode != 0)
            result.Output.Should().Contain("error TRLS067");
    }

    [Fact]
    public async Task Build_DisabledGuard_DoesNotLoadAnalyzers()
    {
        var result = await BuildAsync("_ = new Error.Unexpected(Guid.NewGuid().ToString(\"N\"));",
            includeAnalyzer: false, includeConfiguration: false, enableGuard: false);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().NotContain("TRLS067");
    }

    [Fact]
    public async Task Build_EnabledGuard_PragmaSuppressionRemainsAvailable()
    {
        var result = await BuildAsync("""
            #pragma warning disable TRLS067
            _ = new Error.Unexpected(Guid.NewGuid().ToString("N"));
            #pragma warning restore TRLS067
            """);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().NotContain("TRLS067");
    }

    [Fact]
    public async Task Build_EnabledGuard_DisabledAnalyzersFailsExplicitly()
    {
        var result = await BuildAsync("", runAnalyzers: false);

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("The reason-code guard requires analyzers to run during the build");
    }

    [Fact]
    public void Configuration_StandaloneRuleIsolation_ContainsOnlyOwnedDiagnosticIds()
    {
        var lines = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), "build", "reason-code-guard.globalconfig"));
        var disabledIds = lines
            .Where(line => line.StartsWith("dotnet_diagnostic.", StringComparison.Ordinal)
                && line.EndsWith(".severity = none", StringComparison.Ordinal))
            .Select(line => line["dotnet_diagnostic.".Length..line.IndexOf(".severity", StringComparison.Ordinal)]);
        var analyzers = typeof(PerIncidentReasonCodeAnalyzer).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            .Select(type => (DiagnosticAnalyzer)Activator.CreateInstance(type)!);
        var standaloneIds = analyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics)
            .Select(descriptor => descriptor.Id)
            .Where(id => id != "TRLS067")
            .Distinct(StringComparer.Ordinal);

        disabledIds.Should().BeEquivalentTo(standaloneIds);
        lines.Should().NotContain(line => line.StartsWith("dotnet_analyzer_diagnostic", StringComparison.Ordinal));
        lines.Should().Contain("dotnet_diagnostic.TRLS067.severity = error");
    }

    [Fact]
    public async Task Build_EnabledGuard_MissingAnalyzerFailsExplicitly()
    {
        var result = await BuildAsync("", includeAnalyzer: false);

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("Build Trellis.Analyzers before enabling the reason-code guard");
    }

    [Fact]
    public async Task Build_EnabledGuard_MissingConfigurationFailsExplicitly()
    {
        var result = await BuildAsync("", includeConfiguration: false);

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("Reason-code guard configuration is missing");
    }

    private static async Task<(int ExitCode, string Output)> BuildAsync(
        string statements, bool includeAnalyzer = true, bool includeConfiguration = true,
        bool alreadyLoaded = false, bool enableGuard = true, bool runAnalyzers = true)
    {
        var repository = FindRepositoryRoot();
        var directory = Path.Combine(Path.GetTempPath(), $"trellis reason guard {Guid.NewGuid():N}");
        var projectDirectory = Path.Combine(directory, "Trellis.GuardProbe", "src");
        var analyzerDirectory = Path.Combine(directory, "Trellis.Analyzers", "src", "bin", "Release", "netstandard2.0");
        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(analyzerDirectory);
        Directory.CreateDirectory(Path.Combine(directory, "build"));
        try
        {
            File.Copy(Path.Combine(repository, "Directory.Build.targets"), Path.Combine(directory, "Directory.Build.targets"));
            if (includeConfiguration)
            {
                File.Copy(Path.Combine(repository, "build", "reason-code-guard.globalconfig"),
                    Path.Combine(directory, "build", "reason-code-guard.globalconfig"));
            }

            if (includeAnalyzer)
            {
                File.Copy(typeof(PerIncidentReasonCodeAnalyzer).Assembly.Location,
                    Path.Combine(analyzerDirectory, "Trellis.Analyzers.dll"));
            }

            var project = Path.Combine(projectDirectory, "Trellis.GuardProbe.csproj");
            var analyzerItem = alreadyLoaded
                ? """
                  <ItemGroup>
                    <Analyzer Include="..\..\Trellis.Analyzers\src\bin\Release\netstandard2.0\Trellis.Analyzers.dll" />
                  </ItemGroup>
                  """
                : "";
            File.WriteAllText(project, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <EnableNETAnalyzers>false</EnableNETAnalyzers>
                    <IsPackable>false</IsPackable>
                  </PropertyGroup>
                  {{analyzerItem}}
                </Project>
                """, new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(projectDirectory, "Probe.cs"), $$"""
                using Trellis;

                public static class Probe
                {
                    public static void Emit()
                    {
                        {{statements}}
                    }
                }

                namespace Trellis
                {
                    public readonly struct Result<T> { }
                }

                {{PerIncidentReasonCodeAnalyzerTests.Stubs}}
                """, new UTF8Encoding(true));

            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in new[]
            {
                "build", project, "-c", "Release", "--disable-build-servers", "--verbosity", "quiet",
                $"-p:TrellisEnableReasonCodeGuard={enableGuard}",
                $"-p:RunAnalyzers={runAnalyzers}",
            })
                startInfo.ArgumentList.Add(argument);
            startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the reason-code guard build.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                return (process.ExitCode, await stdout + await stderr);
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

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Trellis.Analyzers", "src", "Trellis.Analyzers.csproj")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not find the repository for the reason-code guard build.");
    }
}