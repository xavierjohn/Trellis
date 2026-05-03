namespace Trellis.Core.Tests.Primitives;

using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Versioning;

public class CorePackageRequiredPrimitiveTests
{
    [Fact]
    public async Task CorePackage_RequiredString_GeneratesJsonAndTraceWithoutPrimitivesReference()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tempRoot = Path.Combine(Path.GetTempPath(), $"trellis-core-package-{Guid.NewGuid():N}");
        var packageDirectory = Path.Combine(tempRoot, "packages");
        var extractedDirectory = Path.Combine(tempRoot, "extracted");
        var consumerDirectory = Path.Combine(tempRoot, "consumer");

        Directory.CreateDirectory(packageDirectory);
        Directory.CreateDirectory(extractedDirectory);
        Directory.CreateDirectory(consumerDirectory);

        try
        {
            var repositoryRoot = GetRepositoryRoot();
            var coreProject = Path.Combine(repositoryRoot, "Trellis.Core", "src", "Trellis.Core.csproj");
            var configuration = GetBuildConfiguration();
            var packageVersion = CreatePackageVersion();

            await RunDotnetAsync(
                repositoryRoot,
                cancellationToken,
                "pack",
                coreProject,
                "--no-build",
                "--configuration",
                configuration,
                "--output",
                packageDirectory,
                $"-p:PackageVersion={packageVersion}",
                "--verbosity",
                "quiet");

            var packagePath = Directory.EnumerateFiles(packageDirectory, "Trellis.Core.*.nupkg").Single();
            AssertCorePackageContainsGenerator(packagePath);
            var coreAssemblyPath = ExtractPackageEntry(packagePath, GetPackageLibEntry("Trellis.Core"), extractedDirectory);
            var coreGeneratorPath = ExtractPackageEntry(packagePath, "analyzers/dotnet/cs/Trellis.Core.Generator.dll", extractedDirectory);

            WriteCoreOnlyConsumerProject(consumerDirectory, packageVersion, coreAssemblyPath, coreGeneratorPath);

            await RestoreProjectAssetsAsync(consumerDirectory, cancellationToken);

            await RunDotnetAsync(
                consumerDirectory,
                cancellationToken,
                "run",
                "--no-restore",
                "--verbosity",
                "quiet");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task PrimitivesPackage_MovedCoreTypesResolveThroughTypeForwarders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tempRoot = Path.Combine(Path.GetTempPath(), $"trellis-primitives-package-{Guid.NewGuid():N}");
        var packageDirectory = Path.Combine(tempRoot, "packages");
        var extractedDirectory = Path.Combine(tempRoot, "extracted");
        var consumerDirectory = Path.Combine(tempRoot, "consumer");

        Directory.CreateDirectory(packageDirectory);
        Directory.CreateDirectory(extractedDirectory);
        Directory.CreateDirectory(consumerDirectory);

        try
        {
            var repositoryRoot = GetRepositoryRoot();
            var coreProject = Path.Combine(repositoryRoot, "Trellis.Core", "src", "Trellis.Core.csproj");
            var primitivesProject = Path.Combine(repositoryRoot, "Trellis.Primitives", "src", "Trellis.Primitives.csproj");
            var configuration = GetBuildConfiguration();
            var packageVersion = CreatePackageVersion();

            await RunDotnetAsync(
                repositoryRoot,
                cancellationToken,
                "pack",
                coreProject,
                "--no-build",
                "--configuration",
                configuration,
                "--output",
                packageDirectory,
                $"-p:PackageVersion={packageVersion}",
                "--verbosity",
                "quiet");

            await RunDotnetAsync(
                repositoryRoot,
                cancellationToken,
                "pack",
                primitivesProject,
                "--no-build",
                "--configuration",
                configuration,
                "--output",
                packageDirectory,
                $"-p:PackageVersion={packageVersion}",
                "--verbosity",
                "quiet");

            var corePackagePath = Directory.EnumerateFiles(packageDirectory, "Trellis.Core.*.nupkg").Single();
            var primitivesPackagePath = Directory.EnumerateFiles(packageDirectory, "Trellis.Primitives.*.nupkg").Single();
            var coreAssemblyPath = ExtractPackageEntry(corePackagePath, GetPackageLibEntry("Trellis.Core"), extractedDirectory);
            var primitivesAssemblyPath = ExtractPackageEntry(primitivesPackagePath, GetPackageLibEntry("Trellis.Primitives"), extractedDirectory);

            WriteTypeForwarderConsumerProject(consumerDirectory, packageVersion, coreAssemblyPath, primitivesAssemblyPath);

            await RestoreProjectAssetsAsync(consumerDirectory, cancellationToken);

            await RunDotnetAsync(
                consumerDirectory,
                cancellationToken,
                "run",
                "--no-restore",
                "--verbosity",
                "quiet");
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static void AssertCorePackageContainsGenerator(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);

        archive.Entries.Select(static entry => entry.FullName)
            .Should().Contain("analyzers/dotnet/cs/Trellis.Core.Generator.dll");
    }

    private static string CreatePackageVersion() => $"0.0.0-smoke-{Guid.NewGuid():N}";

    private static string GetPackageLibEntry(string packageId) => $"lib/{GetTargetFrameworkMoniker()}/{packageId}.dll";

    private static string ExtractPackageEntry(string packagePath, string entryName, string outputDirectory)
    {
        using var archive = ZipFile.OpenRead(packagePath);

        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidOperationException($"Package '{packagePath}' does not contain '{entryName}'.");
        var outputPath = Path.Combine(outputDirectory, entry.Name);
        entry.ExtractToFile(outputPath, overwrite: true);
        return outputPath;
    }

    private static async Task RestoreProjectAssetsAsync(string consumerDirectory, CancellationToken cancellationToken) =>
        await RunDotnetAsync(
            consumerDirectory,
            cancellationToken,
            "restore",
            "-p:RestoreSources=",
            "--verbosity",
            "quiet");

    private static string GetRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Trellis.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate the Trellis repository root from the test output directory.");
    }

    private static string GetBuildConfiguration()
    {
        var assemblyDirectory = new DirectoryInfo(Path.GetDirectoryName(typeof(Result).Assembly.Location)!);
        return assemblyDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Could not infer the current build configuration.");
    }

    private static string GetTargetFrameworkMoniker()
    {
        var frameworkName = typeof(Result).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName
            ?? throw new InvalidOperationException("Could not infer the current target framework.");

        var framework = new FrameworkName(frameworkName);
        return $"net{framework.Version.Major}.{framework.Version.Minor}";
    }

    private static void WriteCoreOnlyConsumerProject(
        string consumerDirectory,
        string packageVersion,
        string coreAssemblyPath,
        string coreGeneratorPath)
    {
        var targetFramework = GetTargetFrameworkMoniker();

        File.WriteAllText(
            Path.Combine(consumerDirectory, "Consumer.csproj"),
            $$"""
              <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                  <OutputType>Exe</OutputType>
                  <TargetFramework>{{targetFramework}}</TargetFramework>
                  <ImplicitUsings>enable</ImplicitUsings>
                  <Nullable>enable</Nullable>
                  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                </PropertyGroup>
                <ItemGroup>
                  <Reference Include="Trellis.Core">
                    <HintPath>{{coreAssemblyPath}}</HintPath>
                  </Reference>
                  <Analyzer Include="{{coreGeneratorPath}}" />
                </ItemGroup>
              </Project>
              """);

        File.WriteAllText(
            Path.Combine(consumerDirectory, "Program.cs"),
            """
            using System.Diagnostics;
            using System.Text.Json;
            using Trellis;

            var captured = new List<Activity>();
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == PrimitiveValueObjectTrace.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => captured.Add(activity)
            };
            ActivitySource.AddActivityListener(listener);

            var code = CoreOnlyCode.Create("abc");
            var json = JsonSerializer.Serialize(code);
            if (json != "\"abc\"")
                throw new InvalidOperationException($"Expected string JSON for generated scalar primitive, got {json}.");

            var roundTrip = JsonSerializer.Deserialize<CoreOnlyCode>(json)
                ?? throw new InvalidOperationException("Generated scalar primitive deserialized to null.");
            if (roundTrip.Value != "abc")
                throw new InvalidOperationException($"Expected round-trip value 'abc', got '{roundTrip.Value}'.");

            var stateJson = JsonSerializer.Serialize(CoreOnlyState.Draft);
            if (stateJson != "\"Draft\"")
                throw new InvalidOperationException($"Expected string JSON for generated enum primitive, got {stateJson}.");

            var stateRoundTrip = JsonSerializer.Deserialize<CoreOnlyState>(stateJson)
                ?? throw new InvalidOperationException("Generated enum primitive deserialized to null.");
            if (stateRoundTrip != CoreOnlyState.Draft)
                throw new InvalidOperationException($"Expected enum round-trip value '{CoreOnlyState.Draft}', got '{stateRoundTrip}'.");

            if (!captured.Any(activity => activity.DisplayName == "CoreOnlyCode.TryCreate"))
                throw new InvalidOperationException("Expected Core-only generated primitive to emit PrimitiveValueObjectTrace activity.");

            if (AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "Trellis.Primitives"))
                throw new InvalidOperationException("Core-only consumer should not load Trellis.Primitives.");

            public sealed partial class CoreOnlyCode : RequiredString<CoreOnlyCode>
            {
            }

            public sealed partial class CoreOnlyState : RequiredEnum<CoreOnlyState>
            {
                public static readonly CoreOnlyState Draft = new();
            }
            """);
    }

    private static void WriteTypeForwarderConsumerProject(
        string consumerDirectory,
        string packageVersion,
        string coreAssemblyPath,
        string primitivesAssemblyPath)
    {
        var targetFramework = GetTargetFrameworkMoniker();

        File.WriteAllText(
            Path.Combine(consumerDirectory, "Consumer.csproj"),
            $$"""
              <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                  <OutputType>Exe</OutputType>
                  <TargetFramework>{{targetFramework}}</TargetFramework>
                  <ImplicitUsings>enable</ImplicitUsings>
                  <Nullable>enable</Nullable>
                  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                </PropertyGroup>
                <ItemGroup>
                  <Reference Include="Trellis.Core">
                    <HintPath>{{coreAssemblyPath}}</HintPath>
                  </Reference>
                  <Reference Include="Trellis.Primitives">
                    <HintPath>{{primitivesAssemblyPath}}</HintPath>
                  </Reference>
                </ItemGroup>
              </Project>
              """);

        File.WriteAllText(
            Path.Combine(consumerDirectory, "Program.cs"),
            """
            using Trellis;
            using Trellis.Primitives;

            _ = typeof(EmailAddress);

            var converterType = Type.GetType("Trellis.ParsableJsonConverter`1, Trellis.Primitives", throwOnError: true)
                ?? throw new InvalidOperationException("Trellis.Primitives did not forward ParsableJsonConverter<T>.");
            if (converterType.Assembly.GetName().Name != "Trellis.Core")
                throw new InvalidOperationException($"Expected ParsableJsonConverter<T> to resolve to Trellis.Core, got {converterType.Assembly.FullName}.");

            var traceType = Type.GetType("Trellis.PrimitiveValueObjectTrace, Trellis.Primitives", throwOnError: true)
                ?? throw new InvalidOperationException("Trellis.Primitives did not forward PrimitiveValueObjectTrace.");
            if (traceType != typeof(PrimitiveValueObjectTrace))
                throw new InvalidOperationException($"Expected PrimitiveValueObjectTrace forwarder to resolve to the Core type, got {traceType.Assembly.FullName}.");

            var closedConverterType = converterType.MakeGenericType(typeof(EmailAddress));
            if (closedConverterType.Assembly.GetName().Name != "Trellis.Core")
                throw new InvalidOperationException($"Expected closed converter type to resolve to Trellis.Core, got {closedConverterType.Assembly.FullName}.");
            """);
    }

    private static async Task RunDotnetAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        if (!process.Start())
            throw new InvalidOperationException("Failed to start dotnet process.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode == 0)
            return;

        throw new InvalidOperationException($"""
            dotnet {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.

            STDOUT:
            {output}

            STDERR:
            {error}
            """);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Could not delete temporary test directory '{path}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"Could not delete temporary test directory '{path}': {ex.Message}");
        }
    }
}
