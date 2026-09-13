namespace Trellis.Primitives.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SourceGenerator;

public class ApiReferenceExampleTests
{
    [Fact]
    public void Taxonomy_CodeExample_CompilesAgainstCurrentApi()
    {
        var markdown = ReadReference("References.Taxonomy.md");
        var section = markdown.IndexOf("## Code examples", StringComparison.Ordinal);
        section.Should().BeGreaterThanOrEqualTo(0);
        var opening = markdown.IndexOf("```csharp", section, StringComparison.Ordinal);
        opening.Should().BeGreaterThanOrEqualTo(0);
        var start = opening + "```csharp".Length;
        var end = markdown.IndexOf("```", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);

        AssertCompiles(markdown[start..end]);
    }

    [Fact]
    public void TraverseAll_ReferenceExample_CompilesAgainstCurrentApi()
    {
        var markdown = ReadReference("References.Core.md");
        var start = markdown.IndexOf("Result<IReadOnlyList<EmailAddress>> emails =", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = markdown.IndexOf(';', start);
        end.Should().BeGreaterThan(start);
        var statement = markdown[start..(end + 1)];

        AssertCompiles($$"""
            using System.Collections.Generic;
            using Trellis;
            using Trellis.Primitives;

            public static class FormValidation
            {
                public static Result<IReadOnlyList<EmailAddress>> Validate(IEnumerable<string> raw)
                {
                    {{statement}}
                    return emails;
                }
            }
            """);
    }

    private static string ReadReference(string name)
    {
        using var stream = typeof(ApiReferenceExampleTests).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded API reference '{name}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void AssertCompiles(string source)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Append(typeof(Result<>).Assembly)
            .Append(typeof(EmailAddress).Assembly)
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static location => MetadataReference.CreateFromFile(location));
        var compilation = CSharpCompilation.Create(
            "ApiReferenceExample",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new RequiredPartialClassGenerator());
        driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation, out var generatorDiagnostics, cancellationToken);

        generatorDiagnostics.Concat(outputCompilation.GetDiagnostics(cancellationToken))
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the published reference example must compile against the current APIs and generator");
    }
}
