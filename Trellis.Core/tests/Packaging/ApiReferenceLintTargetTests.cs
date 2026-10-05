namespace Trellis.Core.Tests.Packaging;

using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

public class ApiReferenceLintTargetTests
{
    [Fact]
    public async Task Lint_missing_PowerShell_reports_an_actionable_prerequisite_error()
    {
        var result = await RunLintAsync(null);

        result.ExitCode.Should().NotBe(0);
        result.Output.Should().Contain("PowerShell 7");
        result.Output.Should().Contain("TrellisPowerShellExecutable");
        result.Output.Should().Contain("PATH");
    }

    [Fact]
    public async Task Lint_explicit_executable_path_with_spaces_is_supported()
    {
        var result = await RunLintAsync(0);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("Trellis lint test runner");
    }

    [Fact]
    public async Task Lint_script_failure_still_fails_the_build()
    {
        var result = await RunLintAsync(1);

        result.ExitCode.Should().NotBe(0);
        result.Output.Should().Contain("Trellis lint test runner");
        result.Output.Should().NotContain("PowerShell 7 could not be started");
    }

    [Theory]
    [InlineData("DesignTimeBuild")]
    [InlineData("BuildingForLiveUnitTesting")]
    public async Task Lint_design_time_build_does_not_require_PowerShell(string property)
    {
        var result = await RunLintAsync(null, property);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().NotContain("PowerShell 7 could not be started");
    }

    private static async Task<(int ExitCode, string Output)> RunLintAsync(int? scriptExitCode, string? excludedBuild = null)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Trellis.slnx")))
            root = root.Parent;
        root.Should().NotBeNull();
        var directory = Path.Combine(Path.GetTempPath(), $"Trellis lint test {Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "test pwsh.cmd" : "test pwsh.sh");
            if (scriptExitCode is { } exitCode)
            {
                var script = OperatingSystem.IsWindows()
                    ? $"@echo off\r\necho Trellis lint test runner\r\nexit /b {exitCode}\r\n"
                    : $"#!/bin/sh\nprintf 'Trellis lint test runner\\n'\nexit {exitCode}\n";
                File.WriteAllText(executable, script, new UTF8Encoding(false));
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var properties = new XElement("PropertyGroup",
                new XElement("TrellisEnableApiReferenceLint", "true"),
                new XElement("TrellisPowerShellExecutable", executable));
            if (excludedBuild is not null)
                properties.Add(new XElement(excludedBuild, "true"));
            var project = new XDocument(new XElement("Project", properties,
                new XElement("Import", new XAttribute("Project", Path.Combine(root!.FullName, "Directory.Build.targets")))));
            var projectPath = Path.Combine(directory, "Lint.proj");
            project.Save(projectPath);
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in new[] { "msbuild", projectPath, "-target:LintApiReference", "-nologo", "-verbosity:minimal" })
                startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start MSBuild.");
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
}