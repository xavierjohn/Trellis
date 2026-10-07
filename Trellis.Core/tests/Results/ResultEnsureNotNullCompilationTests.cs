namespace Trellis.Core.Tests.Results;

using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static ResultEnsureNotNullTests;

public class ResultEnsureNotNullCompilationTests
{
    private static readonly MetadataReference[] _references = BuildReferences();

    public enum SourceKind { Sync, Task, ValueTask }

    public static IEnumerable<object[]> RejectedCases => Cases("int", "Maybe<int>", "Result<int>");

    public static IEnumerable<object[]> AcceptedCases => Cases("int?", "string?");

    public static IEnumerable<object[]> RetiredNullableCases => Cases("int?", "string?")
        .Where(testCase => (GuardForm)testCase[2] != GuardForm.Required);

    public static IEnumerable<object[]> PreservedMaybeCases => Cases("Maybe<int>", "Maybe<string>")
        .Where(testCase => (GuardForm)testCase[2] != GuardForm.Required);

    [Theory]
    [InlineData("Maybe<int>")]
    [InlineData("Maybe<string>")]
    [InlineData("string?")]
    [InlineData("int?")]
    [InlineData("int")]
    [InlineData("Result<int>")]
    public void ToResult_NoErrorArgument_AnyValue_IsRejected(string inputType)
    {
        var diagnostics = CompileConversion(inputType, null, SourceKind.Sync, null);

        diagnostics.Should().ContainSingle(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [MemberData(nameof(RetiredNullableCases))]
    public void ToResult_NullableValue_AnyForm_IsRejected(
        string inputType, SourceKind source, GuardForm form)
    {
        var diagnostics = CompileConversion(inputType, inputType.TrimEnd('?'), source, form);

        diagnostics.Should().ContainSingle(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [MemberData(nameof(PreservedMaybeCases))]
    public void ToResult_MaybeValue_AnyForm_CompilesWithUnwrappedResult(
        string inputType, SourceKind source, GuardForm form)
    {
        var diagnostics = CompileConversion(inputType, inputType[6..^1], source, form);

        diagnostics.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(RejectedCases))]
    public void EnsureNotNull_InferredNonNullableStruct_AnyForm_IsRejected(
        string inputType, SourceKind source, GuardForm form)
    {
        var diagnostics = CompileGuard(inputType, inputType, source, form);

        diagnostics.Should().ContainSingle().Which.Id.Should().Be("CS0452");
    }

    [Theory]
    [MemberData(nameof(AcceptedCases))]
    public void EnsureNotNull_InferredNullableValue_AnyForm_CompilesWithNonNullableResult(
        string inputType, SourceKind source, GuardForm form)
    {
        var diagnostics = CompileGuard(inputType, inputType.TrimEnd('?'), source, form);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void EnsureNotNull_FieldName_NullArguments_CompileWithoutWarnings()
    {
        var diagnostics = Compile("""
            using System.Threading.Tasks;
            using Trellis;

            public static class Consumer
            {
                public static void Run(string? text, int? number, string? fieldName)
                {
                    Result<string> reference = Result.EnsureNotNull(text, fieldName);
                    Result<int> scalar = Result.EnsureNotNull(number, fieldName);
                    Task<Result<string>> referenceTask = Task.FromResult<string?>(text).EnsureNotNullAsync(fieldName);
                    Task<Result<int>> scalarTask = Task.FromResult<int?>(number).EnsureNotNullAsync(fieldName);
                    ValueTask<Result<string>> referenceValueTask = ValueTask.FromResult<string?>(text).EnsureNotNullAsync(fieldName);
                    ValueTask<Result<int>> scalarValueTask = ValueTask.FromResult<int?>(number).EnsureNotNullAsync(fieldName);
                    Error.InvalidInput error = Error.InvalidInput.Required(fieldName);
                    FieldViolation violation = FieldViolation.Required(fieldName);
                }
            }
            """);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void EnsureNotNull_FieldName_PublicAnnotations_AllowNull()
    {
        var parameters = new[] { typeof(Result), typeof(EnsureExtensionsAsync), typeof(Error.InvalidInput), typeof(FieldViolation) }
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.Name is nameof(Result.EnsureNotNull) or nameof(EnsureExtensionsAsync.EnsureNotNullAsync) or nameof(FieldViolation.Required))
            .SelectMany(method => method.GetParameters())
            .Where(parameter => parameter.Name == "fieldName")
            .ToArray();
        var nullability = new NullabilityInfoContext();

        parameters.Should().HaveCount(8);
        parameters.Should().AllSatisfy(parameter =>
            nullability.Create(parameter).ReadState.Should().Be(NullabilityState.Nullable,
                "{0} permits a null field name to target the root", parameter.Member));
    }

    private static IEnumerable<object[]> Cases(params string[] inputTypes)
    {
        foreach (var inputType in inputTypes)
            foreach (var source in Enum.GetValues<SourceKind>())
                foreach (var form in Enum.GetValues<GuardForm>())
                    yield return [inputType, source, form];
    }

    private static ImmutableArray<Diagnostic> CompileGuard(
        string inputType, string outputType, SourceKind source, GuardForm form)
    {
        var argument = form switch
        {
            GuardForm.Error => """new Error.Forbidden("guard.denied")""",
            GuardForm.Factory => """() => new Error.Forbidden("guard.denied")""",
            GuardForm.Required => "\"field\"",
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };
        var (returnType, call) = source switch
        {
            SourceKind.Sync => ($"Result<{outputType}>", $"Result.EnsureNotNull(value, {argument})"),
            SourceKind.Task => ($"Task<Result<{outputType}>>", $"Task.FromResult<{inputType}>(value).EnsureNotNullAsync({argument})"),
            SourceKind.ValueTask => ($"ValueTask<Result<{outputType}>>", $"ValueTask.FromResult<{inputType}>(value).EnsureNotNullAsync({argument})"),
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

        return Compile($$"""
            using System.Threading.Tasks;
            using Trellis;

            public static class Consumer
            {
                public static {{returnType}} Run({{inputType}} value) => {{call}};
            }
            """);
    }

    private static ImmutableArray<Diagnostic> CompileConversion(
        string inputType, string? outputType, SourceKind source, GuardForm? form)
    {
        var argument = form switch
        {
            GuardForm.Error => """new Error.Forbidden("guard.denied")""",
            GuardForm.Factory => """() => new Error.Forbidden("guard.denied")""",
            null => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };
        var call = source switch
        {
            SourceKind.Sync => $"value.ToResult({argument})",
            SourceKind.Task => $"Task.FromResult<{inputType}>(value).ToResultAsync({argument})",
            SourceKind.ValueTask => $"ValueTask.FromResult<{inputType}>(value).ToResultAsync({argument})",
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };
        var statement = outputType is null
            ? $"_ = {call};"
            : $"{(source == SourceKind.Sync ? $"Result<{outputType}>" : $"{source}<Result<{outputType}>>")} result = {call};";

        return Compile($$"""
            using System.Threading.Tasks;
            using Trellis;

            public static class Consumer
            {
                public static void Run({{inputType}} value)
                {
                    {{statement}}
                }
            }
            """);
    }

    private static ImmutableArray<Diagnostic> Compile(string source) =>
        CSharpCompilation.Create(
            "EnsureNotNullConsumer",
            [CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
                cancellationToken: TestContext.Current.CancellationToken)],
            _references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable))
        .GetDiagnostics(TestContext.Current.CancellationToken)
        .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
        .ToImmutableArray();

    private static MetadataReference[] BuildReferences()
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Compiler tests require trusted platform assembly paths.");
        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            references[path] = MetadataReference.CreateFromFile(path);

        references[typeof(Result).Assembly.Location] = MetadataReference.CreateFromFile(typeof(Result).Assembly.Location);
        return references.Values.ToArray();
    }
}
