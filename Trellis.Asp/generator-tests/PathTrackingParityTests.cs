namespace Trellis.AspSourceGenerator.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis.Asp;

/// <summary>
/// §8.2(b) — the runtime install gate and the source generator must agree on which properties get a
/// path-tracking wrapper.
/// </summary>
/// <remarks>
/// <para>
/// The two predicates are independent implementations of one rule: <c>ContainsScalarValueTransitively</c>
/// walks <see cref="Type"/> in <c>ServiceCollectionExtensions</c>, and its twin walks
/// <see cref="ITypeSymbol"/> in <c>PathTrackingRegistryGenerator</c>. Under Native AOT the generated
/// registrations are the <em>only</em> path, because <c>CreatePathTrackingContainerConverter</c> returns
/// <see langword="null"/> once <c>RuntimeFeature.IsDynamicCodeSupported</c> is false. Drift between them
/// is therefore invisible on a JIT test host and shows up as missing field paths in production AOT.
/// </para>
/// <para>
/// The guard is a single corpus compiled once and asked both questions: the generator is run over it,
/// and the same compilation is emitted, loaded, and put through the real reflection modifier. Two
/// hand-maintained corpora would drift exactly the way the predicates do, which is the failure this
/// test exists to catch. This mirrors the <c>RegistrationSurfaceTests</c> discipline the repository
/// already uses for the registration surface.
/// </para>
/// </remarks>
public class PathTrackingParityTests
{
    /// <summary>
    /// Deliberately covers both directions of drift: shapes that must be wrapped (scalar value objects
    /// behind collections and nested objects, converter-backed composite value objects built entirely
    /// from primitives, and collections of them) and shapes that must not (a DTO with no value objects
    /// anywhere, and the interior of a converter-backed value object, whose converter owns its JSON
    /// shape). <c>Boxed</c> pins the case the first version of the predicate got wrong: a
    /// <c>ValueObject</c> with no converter is a plain object to System.Text.Json, so both sides must
    /// still descend into it.
    /// </summary>
    private const string Corpus = """
        using System.Collections.Generic;
        using System.Text.Json.Serialization;
        using Trellis;
        using Trellis.Primitives;

        namespace Corpus
        {
            public sealed class Email : ScalarValueObject<Email, string>, IScalarValue<Email, string>
            {
                private Email(string value) : base(value) { }

                public static Result<Email> TryCreate(string? value, string? fieldName = null) =>
                    Result.Ok(new Email(value!));
            }

            [JsonConverter(typeof(CompositeValueObjectJsonConverter<Address>))]
            public sealed class Address : ValueObject
            {
                private Address(string street) { Street = street; }

                public string Street { get; private set; } = string.Empty;

                public static Result<Address> TryCreate(string street, string? fieldName = null) =>
                    Result.Ok(new Address(street));

                protected override void GetEqualityComponents(ref EqualityComponents components) =>
                    components.Add(Street);
            }

            public sealed class Boxed : ValueObject
            {
                private Boxed(MemberDto inner) { Inner = inner; }

                public MemberDto Inner { get; private set; } = default!;

                protected override void GetEqualityComponents(ref EqualityComponents components) =>
                    components.Add(Inner.Email.Value);
            }

            public sealed record MemberDto(Email Email);

            public sealed record ContactDto(Email Email);

            public sealed record PlainDto(string Name, int Count);

            public sealed record Root(
                List<MemberDto> Members,
                ContactDto Contact,
                Address ShipTo,
                List<Address> Stops,
                Dictionary<string, Address> Prices,
                Dictionary<int, Address> ByCode,
                Boxed Wrapped,
                PlainDto Plain);
        }
        """;

    /// <summary>
    /// The generator discovers roots through <c>[JsonSerializable]</c>, but a partial
    /// <c>JsonSerializerContext</c> only compiles when System.Text.Json's own generator has run. The
    /// declaration is therefore appended for the generator compilation and omitted from the one that is
    /// emitted and loaded — the corpus <em>types</em>, which are what both sides are asked about, stay
    /// single-source.
    /// </summary>
    private const string ContextDeclaration = """

        namespace Corpus
        {
            using System.Text.Json.Serialization;

            [JsonSerializable(typeof(Root))]
            public partial class AppContext : JsonSerializerContext { }
        }
        """;

    [Fact]
    public void The_generator_and_the_reflection_modifier_wrap_the_same_properties()
    {
        var generated = RunGenerator(CreateCompilation(Corpus + ContextDeclaration));
        var fromGenerator = ParseRegistrations(generated);

        var rootType = LoadCorpus(CreateCompilation(Corpus)).GetType("Corpus.Root", throwOnError: true)!;
        var fromReflection = WalkInstalledWrappers(rootType);

        fromReflection.Should().BeEquivalentTo(
            fromGenerator,
            "the AOT registrations are the only path once dynamic code is disabled, so any disagreement "
            + "is a wrapper that silently goes missing (or appears) under Native AOT");
    }

    [Fact]
    public void The_corpus_actually_exercises_both_wrapper_kinds()
    {
        // Guards the guard: an empty-vs-empty comparison would pass while asserting nothing.
        var fromGenerator = ParseRegistrations(RunGenerator(CreateCompilation(Corpus + ContextDeclaration)));

        fromGenerator.Should().Contain("collection:System.Collections.Generic.List<Corpus.MemberDto>,Corpus.MemberDto");
        fromGenerator.Should().Contain("object:Corpus.ContactDto");
        fromGenerator.Should().Contain("object:Corpus.Address");
        fromGenerator.Should().NotContain("object:Corpus.PlainDto");

        // A ValueObject with no converter is a plain object to System.Text.Json, so both sides must
        // descend into it. This is the direction the first version of the predicate got wrong.
        fromGenerator.Should().Contain("object:Corpus.Boxed");
        fromGenerator.Should().Contain("object:Corpus.MemberDto");

        // String-keyed dictionaries are wrapped; non-string keys have no faithful RFC 6901 rendering
        // and must be left alone rather than given a pointer the client cannot map back to its input.
        fromGenerator.Should().Contain("dictionary:System.Collections.Generic.Dictionary<System.String,Corpus.Address>,Corpus.Address");
        fromGenerator.Should().NotContain("dictionary:System.Collections.Generic.Dictionary<System.Int32,Corpus.Address>,Corpus.Address");
    }

    private static SortedSet<string> ParseRegistrations(string generated)
    {
        var results = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(generated, @"RegisterObject<([^>]*(?:<[^>]*>)?[^>]*)>\(\)"))
            results.Add($"object:{Normalize(match.Groups[1].Value)}");

        foreach (Match match in Regex.Matches(generated, @"RegisterCollection<(.+?)>\(\)\s*;"))
        {
            var arguments = SplitTopLevel(match.Groups[1].Value);
            results.Add($"collection:{Normalize(arguments[0])},{Normalize(arguments[1])}");
        }

        foreach (Match match in Regex.Matches(generated, @"RegisterDictionary<(.+?)>\(\)\s*;"))
        {
            var arguments = SplitTopLevel(match.Groups[1].Value);
            results.Add($"dictionary:{Normalize(arguments[0])},{Normalize(arguments[1])}");
        }

        return results;
    }

    // Splits `List<A>, A` on the comma that is not inside angle brackets.
    private static string[] SplitTopLevel(string arguments)
    {
        var depth = 0;
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == '<') depth++;
            else if (arguments[i] == '>') depth--;
            else if (arguments[i] == ',' && depth == 0)
                return [arguments[..i], arguments[(i + 1)..]];
        }

        throw new InvalidOperationException($"Expected two type arguments in '{arguments}'.");
    }

    // The generator emits C# keyword aliases (`string`) where reflection yields CLR names
    // (`System.String`). Canonicalise to the CLR name so the two sides are comparable.
    private static string Normalize(string typeName) =>
        Regex.Replace(
            typeName.Replace("global::", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal),
            @"\b(string|int|long|bool|decimal|double)\b",
            match => match.Value switch
            {
                "string" => "System.String",
                "int" => "System.Int32",
                "long" => "System.Int64",
                "bool" => "System.Boolean",
                "decimal" => "System.Decimal",
                _ => "System.Double",
            });

    private static SortedSet<string> WalkInstalledWrappers(Type root)
    {
        var options = BuildOptions();
        var results = new SortedSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<Type>();

        void Walk(Type type)
        {
            if (!visited.Add(type))
                return;

            JsonTypeInfo typeInfo;
            try
            {
                typeInfo = options.GetTypeInfo(type);
            }
            catch (NotSupportedException)
            {
                return;
            }

            foreach (var property in typeInfo.Properties)
            {
                var converterType = property.CustomConverter?.GetType();
                if (converterType is { IsGenericType: true })
                {
                    var arguments = converterType.GetGenericArguments();
                    if (converterType.Name.StartsWith("PathTrackingObjectConverter", StringComparison.Ordinal))
                    {
                        results.Add($"object:{Display(arguments[0])}");
                        Walk(arguments[0]);
                        continue;
                    }

                    if (converterType.Name.StartsWith("PathTrackingCollectionConverter", StringComparison.Ordinal))
                    {
                        results.Add($"collection:{Display(arguments[0])},{Display(arguments[1])}");
                        Walk(arguments[1]);
                        continue;
                    }

                    if (converterType.Name.StartsWith("PathTrackingDictionaryConverter", StringComparison.Ordinal))
                    {
                        results.Add($"dictionary:{Display(arguments[0])},{Display(arguments[1])}");
                        Walk(arguments[1]);
                        continue;
                    }
                }

                Walk(property.PropertyType);
            }
        }

        Walk(root);
        return results;
    }

    private static string Display(Type type)
    {
        if (!type.IsGenericType)
            return type.FullName!;

        var definition = type.GetGenericTypeDefinition().FullName!;
        var name = definition[..definition.IndexOf('`', StringComparison.Ordinal)];
        var arguments = string.Join(",", type.GetGenericArguments().Select(Display));
        return $"{name}<{arguments}>";
    }

    private static JsonSerializerOptions BuildOptions()
    {
        var services = new ServiceCollection();
        services.AddScalarValueValidationForMinimalApi();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
    }

    private static Assembly LoadCorpus(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue(
            "the parity corpus must compile: {0}",
            string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        return Assembly.Load(stream.ToArray());
    }

    private static string RunGenerator(CSharpCompilation compilation)
    {
        var driver = CSharpGeneratorDriver.Create(new PathTrackingRegistryGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics, TestContext.Current.CancellationToken);

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the source generator should not produce errors");

        var generated = driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .Where(s => s.HintName.Contains("PathTracking", StringComparison.Ordinal))
            .Select(s => s.SourceText.ToString())
            .FirstOrDefault();

        generated.Should().NotBeNull("the corpus contains wrappable properties");
        return generated!;
    }

    private static CSharpCompilation CreateCompilation(string source) =>
        CSharpCompilation.Create(
            assemblyName: "PathTrackingParityCorpus",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            references: AppDomain.CurrentDomain.GetAssemblies()
                .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                .Select(static assembly => assembly.Location)
                .Concat([
                    typeof(ScalarValueObject<,>).Assembly.Location,
                    typeof(Trellis.Primitives.CompositeValueObjectJsonConverter<>).Assembly.Location,
                    typeof(Trellis.Asp.ValidationErrorsContext).Assembly.Location,
                    typeof(JsonSerializer).Assembly.Location,
                ])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(static location => MetadataReference.CreateFromFile(location))
                .ToArray(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
