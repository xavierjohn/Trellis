namespace Trellis.Core.Tests.Packaging;

using Markdig;
using Markdig.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Trellis.Docs.Audit;
using Trellis.Testing;

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
    [InlineData("trellis-api-analyzers.md", "## Compilable examples", OutputKind.DynamicallyLinkedLibrary)]
    [InlineData("trellis-api-testing-reference.md", "### FakeRepository", OutputKind.ConsoleApplication)]
    public void Compile_ReferenceExample_CurrentApis_HasNoCompilerErrors(string document, string section, OutputKind outputKind)
    {
        var platformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("The runtime must provide its platform assembly paths.");
        var references = platformAssemblies.Split(Path.PathSeparator)
            .Append(typeof(Result).Assembly.Location)
            .Append(typeof(FakeRepository<,>).Assembly.Location)
            .Append(typeof(FluentAssertions.AssertionExtensions).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ApiReferenceExample",
            [CSharpSyntaxTree.ParseText(ReadReferenceExample(document, section, "## Compilable examples"),
                cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(outputKind, nullableContextOptions: NullableContextOptions.Enable));

        using var assembly = new MemoryStream();
        var result = compilation.Emit(assembly, cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue($"{document} {section} must compile: {string.Join(Environment.NewLine, result.Diagnostics)}");
    }

    [Theory]
    [InlineData("### Use `FireResult` on a regular Stateless machine")]
    [InlineData("### Use `LazyStateMachine<TState, TTrigger>`")]
    public void Parse_ReferenceExample_StateMachine_TopLevelStatementsPrecedeTypes(string section)
    {
        var syntax = CSharpSyntaxTree.ParseText(
            ReadReferenceExample("trellis-api-statemachine.md", section, "## Code examples"),
            cancellationToken: TestContext.Current.CancellationToken);
        var root = syntax.GetCompilationUnitRoot(TestContext.Current.CancellationToken);

        root.Members.SkipWhile(member => member is GlobalStatementSyntax)
            .Should().NotContain(member => member is GlobalStatementSyntax,
                "C# top-level statements must precede type declarations");
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

    private static string ReadReferenceExample(string document, string section, string parentSection)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Trellis.slnx")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the test must locate the repository root");
        var markdown = File.ReadAllText(Path.Combine(directory!.FullName, "docs", "docfx_project", "api_reference", document))
            .ReplaceLineEndings("\n");
        var parentStart = markdown.IndexOf(parentSection + "\n", StringComparison.Ordinal);
        parentStart.Should().BeGreaterThanOrEqualTo(0, $"{document} must contain {parentSection}");
        var start = markdown.IndexOf(section + "\n", parentStart, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{document} must contain {section}");
        var level = section.TakeWhile(character => character == '#').Count();

        return Markdown.Parse(markdown[start..]).Skip(1)
            .TakeWhile(block => block is not HeadingBlock heading || heading.Level > level)
            .OfType<FencedCodeBlock>()
            .First(block => string.Equals(block.Info, "csharp", StringComparison.OrdinalIgnoreCase))
            .Lines.ToString();
    }

    private static async Task<(int ExitCode, string Output)> RunAuditAsync(string documentation)
    {
        using var fixture = new DocumentationAuditFixture();
        fixture.WritePackage("Primary", """
            public sealed class Probe
            {
                public int Id => 1;
            }
            public static class AuditSentinel
            {
                public static void Ping() { }
            }
            """, documentation + "\n\n```csharp\nAuditSentinel.Ping();\n```\n");
        fixture.WritePackage("Neighbor", "public sealed class Neighbor { }", "### Neighbor\n\n`Probe.Id`\n");
        return await fixture.RunAsync();
    }
}