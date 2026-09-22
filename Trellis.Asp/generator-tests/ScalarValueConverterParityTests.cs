namespace Trellis.AspSourceGenerator.Tests;

using System.Reflection;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Trellis;
using Trellis.Asp;
using Trellis.Asp.Validation;

public sealed class ScalarValueConverterParityTests
{
    private static readonly (Type Primitive, string SourceType, string ValidJson)[] Primitives =
    [
        (typeof(string), "string", "\"value\""),
        (typeof(int), "int", "1"),
        (typeof(long), "long", "1"),
        (typeof(short), "short", "1"),
        (typeof(byte), "byte", "1"),
        (typeof(float), "float", "1"),
        (typeof(double), "double", "1"),
        (typeof(decimal), "decimal", "1"),
        (typeof(bool), "bool", "true"),
        (typeof(Guid), "System.Guid", "\"00000000-0000-0000-0000-000000000001\""),
        (typeof(DateTime), "System.DateTime", "\"2026-01-02T03:04:05Z\""),
        (typeof(DateTimeOffset), "System.DateTimeOffset", "\"2026-01-02T03:04:05+00:00\""),
    ];

    private static readonly Lazy<Assembly> GeneratedAssembly = new(CompileConverters);

    public static IEnumerable<object[]> Cases()
    {
        for (var i = 0; i < Primitives.Length; i++)
        {
            foreach (var json in new[] { "null", "{}", "\"\"", "\"  \"", "\"invalid\"", "1e100", Primitives[i].ValidJson })
                yield return [i, json, "/items/0/amount"];

            yield return [i, Primitives[i].ValidJson, "unexpected"];
            yield return [i, Primitives[i].ValidJson, "located"];
            yield return [i, Primitives[i].ValidJson, "success"];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ReadWrite_GeneratedConverter_MatchesReflection(int index, string json, string fieldName)
    {
        var type = GeneratedAssembly.Value.GetType($"Parity.Probe{index}", throwOnError: true)!;
        var factory = (JsonConverterFactory)Activator.CreateInstance(
            GeneratedAssembly.Value.GetType("Trellis.Generated.GeneratedValueObjectConverterFactory", true)!)!;
        var reflection = (JsonConverter)Activator.CreateInstance(
            typeof(ValidatingJsonConverter<,>).MakeGenericType(type, Primitives[index].Primitive))!;

        var expected = Read(type, reflection, json, fieldName);
        var actual = Read(type, factory, json, fieldName);

        actual.Should().BeEquivalentTo(expected);
        if (fieldName == "success")
            actual.Json.Should().NotBeNull();
        else
            actual.Errors.Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ReadWrite_MaybeConverter_MatchesGeneratedExceptOptionalNull(int index, string json, string fieldName)
    {
        var type = GeneratedAssembly.Value.GetType($"Parity.Probe{index}", true)!;
        var factory = (JsonConverterFactory)Activator.CreateInstance(
            GeneratedAssembly.Value.GetType("Trellis.Generated.GeneratedValueObjectConverterFactory", true)!)!;
        var maybeType = typeof(Maybe<>).MakeGenericType(type);
        var maybeConverter = (JsonConverter)Activator.CreateInstance(
            typeof(MaybeScalarValueJsonConverter<,>).MakeGenericType(type, Primitives[index].Primitive))!;

        var actual = Read(maybeType, maybeConverter, json, fieldName);

        if (json == "null")
        {
            actual.Errors.Should().BeNull();
            actual.Json.Should().Be("null");
        }
        else
        {
            var required = Read(type, factory, json, fieldName);
            actual.Errors.Should().BeEquivalentTo(required.Errors);
            actual.RejectedShape.Should().Be(required.RejectedShape);
            actual.Json.Should().Be(required.Json ?? (required.RejectedShape ? null : "null"));
        }
    }

    private static (Error.InvalidInput? Errors, string? Json, bool RejectedShape) Read(Type type, JsonConverter converter, string json, string fieldName)
    {
        using var scope = ValidationErrorsContext.BeginScope();
        ValidationErrorsContext.CurrentPropertyName = fieldName;
        var options = OptionsFor(converter);
        object? value;
        try
        {
            value = JsonSerializer.Deserialize(json, type, options);
        }
        catch (JsonException) when (json == "{}")
        {
            return (ValidationErrorsContext.GetUnprocessableContent(), null, true);
        }

        return (ValidationErrorsContext.GetUnprocessableContent(),
            value is null ? null : JsonSerializer.Serialize(value, type, options), false);
    }

    private static JsonSerializerOptions OptionsFor(JsonConverter converter)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(converter);
        return options;
    }

    private static Assembly CompileConverters()
    {
        var source = new StringBuilder("using System; using Trellis; namespace Parity;");
        for (var i = 0; i < Primitives.Length; i++)
        {
            var primitive = Primitives[i].SourceType;
            source.Append(CultureInfo.InvariantCulture, $$"""
                public sealed class Probe{{i}} : ScalarValueObject<Probe{{i}}, {{primitive}}>, IScalarValue<Probe{{i}}, {{primitive}}>
                {
                    private Probe{{i}}({{primitive}} value) : base(value) { }
                    public static Result<Probe{{i}}> TryCreate({{primitive}} value, string? fieldName = null) =>
                        fieldName == "success"
                            ? Result.Ok(new Probe{{i}}(value))
                            : fieldName == "unexpected"
                            ? Result.Fail<Probe{{i}}>(new Error.Unexpected("test.failure") { Detail = "Safe detail." })
                            : Result.Fail<Probe{{i}}>(Error.InvalidInput.ForField(
                                "test.rejected",
                                fieldName == "located" ? InputPointer.ForQuery("external") : InputPointer.ForProperty(fieldName ?? "value"),
                                ValidationArgs.Of("min", 1), "Rejected."));
                """);
            if (primitive != "string")
                source.Append(CultureInfo.InvariantCulture, $"public static Result<Probe{i}> TryCreate(string? value, string? fieldName = null) => throw new NotSupportedException();");
            source.Append('}');
        }

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => a.Location)
            .Concat([typeof(ScalarValueObject<,>).Assembly.Location, typeof(ValidationErrorsContext).Assembly.Location])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(location => MetadataReference.CreateFromFile(location));
        var compilation = CSharpCompilation.Create(
            "ScalarValueConverterParity",
            [CSharpSyntaxTree.ParseText(source.ToString())],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ScalarValueJsonConverterGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        using var stream = new MemoryStream();
        var emitted = output.Emit(stream);
        emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        emitted.Success.Should().BeTrue();
        return Assembly.Load(stream.ToArray());
    }
}