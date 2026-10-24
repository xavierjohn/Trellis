namespace Trellis.AspSourceGenerator.Tests;

using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Trellis.Asp;
using Trellis.Primitives;

internal static class GeneratorTestReferences
{
    internal static ImmutableArray<MetadataReference> All { get; } = CreateReferences();

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        Assembly[] assemblies =
        [
            typeof(object).Assembly,
            Assembly.Load("System.Runtime"),
            Assembly.Load("System.Collections"),
            typeof(ImmutableArray).Assembly,
            typeof(Enumerable).Assembly,
            typeof(JsonSerializer).Assembly,
            typeof(StringLengthAttribute).Assembly,
            typeof(ScalarValueObject<,>).Assembly,
            typeof(ValidationErrorsContext).Assembly,
            typeof(CompositeValueObjectJsonConverter<>).Assembly,
        ];

        return assemblies
            .Select(static assembly => assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static location => MetadataReference.CreateFromFile(location))
            .ToImmutableArray<MetadataReference>();
    }
}