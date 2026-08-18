namespace Trellis.Core.Tests;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

/// <summary>
/// Pins the fail-fast behavior of <see cref="ResultRequiresExplicitHttpMappingConverter"/>:
/// direct JSON serialization / deserialization of <see cref="Result{TValue}"/> (or an
/// <see cref="IResult"/> / <see cref="IResult{TValue}"/>-declared receiver) throws an
/// actionable <see cref="NotSupportedException"/> instead of silently producing a struct
/// dump. The intended path is <c>.ToHttpResponse()</c> (Trellis.Asp) — proven by checking
/// the message wording — and consumers who legitimately want raw JSON of a Result can
/// override via <c>JsonSerializerOptions.Converters</c>. The override must match the
/// declared static type; the per-shape rule and the <see cref="System.Text.Json.Serialization.JsonConverterFactory"/>
/// alternative are pinned in dedicated tests below.
/// </summary>
public class ResultJsonFailFastTests
{
    [Fact]
    public void Serialize_Result_throws_with_actionable_message()
    {
        var result = Result.Ok(42);

        var act = () => JsonSerializer.Serialize(result);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Result<Int32>*cannot be JSON-serialized*ToHttpResponse*");
    }

    [Fact]
    public void Deserialize_Result_throws_with_actionable_message()
    {
        var act = () => JsonSerializer.Deserialize<Result<int>>("{\"IsSuccess\":true,\"Value\":42}");

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Result<Int32>*cannot be JSON-serialized or deserialized*ToHttpResponse*");
    }

    [Fact]
    public void Serialize_failure_Result_also_throws_with_actionable_message()
    {
        var result = Result.Fail<int>(new Error.NotFound(new ResourceRef("Order", "42")));

        var act = () => JsonSerializer.Serialize(result);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Result<Int32>*cannot be JSON-serialized*Match*TryGetValue*");
    }

    [Fact]
    public void Consumer_override_in_options_takes_precedence_over_default_throwing_converter()
    {
        // Legitimate escape hatch: consumer who really wants to serialize Result<T> (logging,
        // IPC, storage) registers a JsonConverter<Result<T>> in JsonSerializerOptions.Converters.
        // STJ's converter resolution preferences the option-registered converter over the
        // type's [JsonConverter] attribute, so the throwing default is bypassed.
        var result = Result.Ok(42);

        var json = JsonSerializer.Serialize(result, s_optionsWithRawResultConverter);

        json.Should().Be("\"OK:42\"");
    }

    [Fact]
    public void Serializing_a_DTO_containing_Result_property_also_throws()
    {
        // The default object formatter recursively serializes properties — the throw fires on
        // the nested Result<T> property exactly the same as a top-level Result<T>.
        var dto = new DtoCarryingResult(Result.Ok(42));

        var act = () => JsonSerializer.Serialize(dto);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Result<Int32>*ToHttpResponse*");
    }

    // ---------- Interface-declared return-type coverage ----------

    [Fact]
    public void Serialize_via_IResult_T_interface_also_throws()
    {
        // STJ resolves [JsonConverter] against the static declared type. A controller signature
        // like Task<IResult<int>> GetAsync() would previously bypass the throwing converter
        // because the attribute was only on the struct, not the interface — re-creating the
        // exact silent-struct-dump bug this PR is meant to prevent. The attribute now lives on
        // both the struct AND the interfaces to close the gap. The exception message names
        // the *declared* shape so the consumer sees IResult<Int32>, not Result<Int32>.
        IResult<int> result = Result.Ok(42);

        var act = () => JsonSerializer.Serialize(result);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*IResult<Int32>*ToHttpResponse*");
    }

    [Fact]
    public void Serialize_DTO_with_IResult_T_property_also_throws()
    {
        var dto = new DtoCarryingIResultT(Result.Ok(42));

        var act = () => JsonSerializer.Serialize(dto);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*IResult<Int32>*ToHttpResponse*");
    }

    [Fact]
    public void Serialize_via_non_generic_IResult_also_throws()
    {
        IResult result = Result.Ok(42);

        var act = () => JsonSerializer.Serialize(result);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*IResult*ToHttpResponse*");
    }

    // ---------- Override path is per-declared-shape ----------

    [Fact]
    public void JsonConverter_for_Result_T_does_not_override_IResult_T_declaration()
    {
        // The override path is per-declared-static-type. A JsonConverter<Result<int>> in
        // options.Converters does NOT match an IResult<int>-declared value, so the interface
        // attribute still fires and throws. This is the trap the exception message warns
        // about and that the docs now spell out.
        IResult<int> result = Result.Ok(42);

        var act = () => JsonSerializer.Serialize(result, s_optionsWithRawResultConverter);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*IResult<Int32>*");
    }

    [Fact]
    public void Consumer_can_override_IResult_T_via_options_registered_converter()
    {
        // Correct shape: register a converter typed for the declared shape.
        IResult<int> result = Result.Ok(42);

        var json = JsonSerializer.Serialize(result, s_optionsWithRawIResultTConverter);

        json.Should().Be("\"IResult:OK\"");
    }

    [Fact]
    public void Consumer_can_override_all_result_shapes_with_a_factory()
    {
        // Mixed-shape override: a single JsonConverterFactory whose CanConvert covers every
        // result shape lets the consumer route all of them through one custom path.
        IResult<int> ir = Result.Ok(42);
        var concrete = Result.Ok(99);

        JsonSerializer.Serialize(ir, s_optionsWithFactoryOverride).Should().Be("\"any:result\"");
        JsonSerializer.Serialize(concrete, s_optionsWithFactoryOverride).Should().Be("\"any:result\"");
    }

    private sealed record DtoCarryingResult(Result<int> Inner);

    private sealed record DtoCarryingIResultT(IResult<int> Inner);

    private static readonly JsonSerializerOptions s_optionsWithRawResultConverter = new()
    {
        Converters = { new RawResultConverter() },
    };

    private static readonly JsonSerializerOptions s_optionsWithRawIResultTConverter = new()
    {
        Converters = { new RawIResultTConverter() },
    };

    private static readonly JsonSerializerOptions s_optionsWithFactoryOverride = new()
    {
        Converters = { new AllResultShapesFactory() },
    };

    private sealed class RawResultConverter : JsonConverter<Result<int>>
    {
        public override Result<int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotImplementedException();

        public override void Write(Utf8JsonWriter writer, Result<int> value, JsonSerializerOptions options)
        {
            value.TryGetValue(out var v);
            writer.WriteStringValue($"OK:{v}");
        }
    }

    private sealed class RawIResultTConverter : JsonConverter<IResult<int>>
    {
        public override IResult<int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotImplementedException();

        public override void Write(Utf8JsonWriter writer, IResult<int> value, JsonSerializerOptions options) =>
            writer.WriteStringValue("IResult:OK");
    }

    private sealed class AllResultShapesFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            if (typeToConvert == typeof(IResult)) return true;
            if (!typeToConvert.IsGenericType) return false;
            var def = typeToConvert.GetGenericTypeDefinition();
            return def == typeof(Result<>) || def == typeof(IResult<>);
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            new ConstantStringConverter();

        private sealed class ConstantStringConverter : JsonConverter<object>
        {
            public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                throw new NotImplementedException();

            public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options) =>
                writer.WriteStringValue("any:result");

            public override bool CanConvert(Type typeToConvert) => true;
        }
    }
}