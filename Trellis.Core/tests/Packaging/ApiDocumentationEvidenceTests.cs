namespace Trellis.Core.Tests.Packaging;

using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Trellis.Docs.Audit;

public class ApiDocumentationEvidenceTests
{
    [Theory]
    [InlineData("### Result<T>", "Result")]
    [InlineData("Result<T>\n=========", "Result")]
    [InlineData("### **Result**", "Result")]
    [InlineData("### Re**sult**", "Result")]
    [InlineData("### [Result](reference.md#result)", "Result")]
    [InlineData("### N&#97;me", "Name")]
    [InlineData("### N&#x61;me", "Name")]
    [InlineData("### Name&nbsp;Value", "Name")]
    [InlineData("Returns `Result<T>`.", "Result")]
    [InlineData("Returns `\u00c9tat`.", "\u00c9tat")]
    [InlineData("Use `Trellis.Result.Ok(value)`.", "Ok")]
    [InlineData("Use `Maybe<T>.None`.", "None")]
    [InlineData("Use ``Result`T``.", "Result")]
    [InlineData("Use `Result\n<T>`.", "Result")]
    [InlineData("| Member | Meaning |\n| --- | --- |\n| Name | Display text. |", "Name")]
    [InlineData("| Member | Meaning |\n| --- | --- |\n| N&#97;me | Display text. |", "Name")]
    [InlineData("Member | Meaning\n--- | ---\nId | Identifier.", "Id")]
    [InlineData("| Signature | Meaning |\n| --- | --- |\n| `public int Value { get; }` | Payload. |", "Value")]
    [InlineData("```csharp\npublic Result<T> Create<T>() => Result.Ok<T>();\n```", "Create")]
    [InlineData("~~~cs\nreturn Maybe<string>.None;\n~~~", "None")]
    [InlineData("   ````c#\n   return OrderId.New();\n   `````", "New")]
    [InlineData("```CSHARP\nreturn value.Id;\n```", "Id")]
    [InlineData("```csharp\nreturn value.Name;", "Name")]
    [InlineData("```csharp\nvar marker = \"<!--\";\nreturn value.Id;\n```", "Id")]
    [InlineData("```csharp\nreturn nameof(Value);\n```", "Value")]
    public void Contains_StructuredText_CompleteIdentifier_IsDocumented(string markdown, string name) =>
        new ApiDocumentationEvidence(markdown).Contains(name).Should().BeTrue();

    [Theory]
    [InlineData("The result contains a value.", "Result")]
    [InlineData("The Id is assigned automatically.", "Id")]
    [InlineData("New instances have no value.", "New")]
    [InlineData("None of these values are valid.", "None")]
    [InlineData("The Name is display text.", "Name")]
    [InlineData("The N&#97;me is display text.", "Name")]
    [InlineData("The Value is returned to the caller.", "Value")]
    [InlineData("Each Page has a continuation token.", "Page")]
    [InlineData("---\ntypes: [Result]\n---\nOrdinary prose.", "Result")]
    [InlineData("---\ntypes: [`Name`]\n---\nOrdinary prose.", "Name")]
    [InlineData("<!-- ### Result -->", "Result")]
    [InlineData("<!--\n`Id`\n### Name\n-->", "Id")]
    [InlineData("<!--\n`Id`\n### Name\n-->", "Name")]
    [InlineData("Use [details](reference.md#Name).", "Name")]
    [InlineData("### [Details](reference.md#Name)", "Name")]
    [InlineData("[details]: reference.md#Name", "Name")]
    [InlineData("Use [Result](reference.md).", "Result")]
    [InlineData("![`Name`](image.png)", "Name")]
    [InlineData("### ![`Name`](image.png)", "Name")]
    [InlineData("Use <https://example.com/Name>.", "Name")]
    [InlineData("### <https://example.com/Name>", "Name")]
    [InlineData("### https://example.com/Name", "Name")]
    [InlineData("Use \\`Result\\` literally.", "Result")]
    [InlineData("Use `Result without closing.", "Result")]
    [InlineData("    `Name`", "Name")]
    [InlineData("    # Name", "Name")]
    [InlineData("Member | Meaning\nName | Text", "Name")]
    [InlineData("```text\n### Name\n`Name`\n```", "Name")]
    [InlineData("```json\n{\"Name\": \"value\"}\n```", "Name")]
    [InlineData("```\n`Name`\n```", "Name")]
    [InlineData("```csharp Name\nreturn value;\n```", "Name")]
    [InlineData("```csharp\n// Result\nreturn value;\n```", "Result")]
    [InlineData("```csharp\n/* Name */\nreturn value;\n```", "Name")]
    [InlineData("```csharp\nvar text = \"Value\";\n```", "Value")]
    [InlineData("```csharp\nvar text = @\"Name\";\n```", "Name")]
    [InlineData("```csharp\nvar text = $\"Name {value.Id}\";\n```", "Id")]
    [InlineData("```csharp\nvar text = \"\"\"Value\"\"\";\n```", "Value")]
    [InlineData("```csharp\nvar letter = 'I';\n```", "I")]
    [InlineData("Use `\"Name\"` as a wire value.", "Name")]
    [InlineData("Use `/* Name */` as a comment.", "Name")]
    [InlineData("", "Name")]
    [InlineData(" \n\t", "Name")]
    public void Contains_IncidentalText_CommonApiNames_AreNotDocumented(string markdown, string name) =>
        new ApiDocumentationEvidence(markdown).Contains(name).Should().BeFalse();

    [Theory]
    [InlineData("### ResultExtensions", "Result")]
    [InlineData("### Result**Extensions**", "Result")]
    [InlineData("### Full**Name**", "Name")]
    [InlineData("### SomeId", "Id")]
    [InlineData("### Name&#50;", "Name")]
    [InlineData("### &#x32;Name", "Name")]
    [InlineData("### &eacute;Name", "Name")]
    [InlineData("Use `TryNew`.", "New")]
    [InlineData("Use `NoneValue`.", "None")]
    [InlineData("Use `Name2`.", "Name")]
    [InlineData("Use `2Name`.", "Name")]
    [InlineData("Use `Name_suffix`.", "Name")]
    [InlineData("Use `_Name`.", "Name")]
    [InlineData("Use `\u00e9Name`.", "Name")]
    [InlineData("Use `Name\u00e9`.", "Name")]
    [InlineData("Use `result<T>`.", "Result")]
    [InlineData("Use `new`.", "New")]
    [InlineData("Use `id`.", "Id")]
    [InlineData("| Member | Meaning |\n| --- | --- |\n| FullName | Display text. |", "Name")]
    [InlineData("| Member | Meaning |\n| --- | --- |\n| Name&#50; | Display text. |", "Name")]
    [InlineData("```csharp\nreturn item.Identifier;\n```", "Id")]
    public void Contains_StructuredText_LongerOrDifferentlyCasedName_IsNotDocumented(string markdown, string name) =>
        new ApiDocumentationEvidence(markdown).Contains(name).Should().BeFalse();

    [Fact]
    public void Contains_DifferentDocuments_Evidence_RemainsIsolated()
    {
        var owner = new ApiDocumentationEvidence("### OtherType");
        var neighbor = new ApiDocumentationEvidence("### Result<T>\n\n`Id`");

        neighbor.Contains("Result").Should().BeTrue();
        neighbor.Contains("Id").Should().BeTrue();
        owner.Contains("Result").Should().BeFalse();
        owner.Contains("Id").Should().BeFalse();
    }

    [Fact]
    public void BlankCommentsAndLiterals_CodeAndTrivia_OffsetsAndLines_ArePreserved()
    {
        const string source = "// Name\r\nvalue.Id /* Value */;\r\nvar text = \"Name\";\r\nvalue.New();";

        var blanked = CSharpCode.BlankCommentsAndLiterals(source);

        blanked.Length.Should().Be(source.Length);
        blanked.IndexOf("value.Id", StringComparison.Ordinal).Should().Be(source.IndexOf("value.Id", StringComparison.Ordinal));
        blanked.IndexOf("value.New", StringComparison.Ordinal).Should().Be(source.IndexOf("value.New", StringComparison.Ordinal));
        Enumerable.Range(0, source.Length).Where(index => source[index] == '\n')
            .Should().Equal(Enumerable.Range(0, blanked.Length).Where(index => blanked[index] == '\n'));
        blanked.Should().NotContain("Name").And.NotContain("Value");
    }

    [Theory]
    [InlineData("The Id is assigned automatically.")]
    [InlineData("### Identity")]
    [InlineData("### Id&#50;")]
    [InlineData("Use `id`.")]
    [InlineData("No members are named here.")]
    public async Task Audit_OwningPackage_MissingMember_FailsEvenWhenAnotherReferenceNamesIt(string markdown)
    {
        var result = await RunAuditAsync("### Probe\n\n" + markdown);

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("error TRLDOC008: Primary has 0 undocumented type(s) and 1 undocumented member signature(s).");
        result.Output.Should().NotContain("error TRLDOC005");
        result.Output.Should().NotContain("error TRLDOC014");
        result.Output.Should().NotContain("error TRLDOC015");
    }

    [Theory]
    [InlineData("Probe is a public type.")]
    [InlineData("### ProbeExtensions")]
    public async Task Audit_OwningPackage_MissingType_FailsEvenWhenAnotherReferenceNamesIt(string markdown)
    {
        var result = await RunAuditAsync(markdown + "\n\n`Id`");

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("error TRLDOC008: Primary has 1 undocumented type(s)");
    }

    [Fact]
    public async Task Audit_OwningPackage_StructuredTypeAndMember_Passes()
    {
        var result = await RunAuditAsync("### Probe\n\n| Member | Meaning |\n| --- | --- |\n| Id | Identifier. |");

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().NotContain("error TRLDOC008");
    }

    private static async Task<(int ExitCode, string Output)> RunAuditAsync(string documentation)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Trellis doc evidence {Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var docsDirectory = Path.Combine(directory, "docs", "docfx_project", "api_reference");
            Directory.CreateDirectory(Path.Combine(docsDirectory, "audit-completeness"));
            File.WriteAllText(Path.Combine(docsDirectory, "audit-completeness", "ambiguous-members.txt"), "", new UTF8Encoding(true));
            WritePackage(directory, "Primary", """
                public sealed class Probe
                {
                    public int Id => 1;
                }
                public static class AuditSentinel
                {
                    public static void Ping() { }
                }
                """);
            WritePackage(directory, "Neighbor", "public sealed class Neighbor { }");
            File.WriteAllText(Path.Combine(docsDirectory, "trellis-api-primary.md"),
                documentation + "\n\n```csharp\nAuditSentinel.Ping();\n```\n", new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(docsDirectory, "trellis-api-neighbor.md"),
                "### Neighbor\n\n`Probe.Id`\n", new UTF8Encoding(true));

            var testAssembly = typeof(ApiDocumentationEvidenceTests).Assembly.Location;
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in new[]
            {
                "exec",
                "--runtimeconfig", Path.ChangeExtension(testAssembly, "runtimeconfig.json"),
                "--depsfile", Path.ChangeExtension(testAssembly, "deps.json"),
                typeof(ApiDocumentationEvidence).Assembly.Location,
            })
                startInfo.ArgumentList.Add(argument);
            startInfo.Environment["TRELLIS_FW_ROOT"] = directory;
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the completeness audit.");
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

    private static void WritePackage(string directory, string name, string source)
    {
        var projectDirectory = Path.Combine(directory, name, "src");
        var assemblyDirectory = Path.Combine(projectDirectory, "bin", "Release", "net10.0");
        Directory.CreateDirectory(assemblyDirectory);
        new XDocument(new XElement("Project", new XElement("PropertyGroup",
            new XElement("TrellisApiRefName", name.ToLowerInvariant()))))
            .Save(Path.Combine(projectDirectory, name + ".csproj"));

        var compilation = CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = File.Create(Path.Combine(assemblyDirectory, name + ".dll"));
        var result = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.Diagnostics));
    }
}