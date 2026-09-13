namespace Trellis.Showcase.MinimalApi;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trellis.Asp;
using Trellis.Asp.Validation;
using Trellis.Generated;

internal static class ScalarConverterSmokeProbe
{
    private static readonly JsonSerializerOptions Options = new();

    public static void Run()
    {
        var generated = (JsonConverter<SmokeQuantity>)new GeneratedValueObjectConverterFactory()
            .CreateConverter(typeof(SmokeQuantity), Options)!;
        var runtime = new ValidatingJsonConverter<SmokeQuantity, int>();
        var optional = new MaybeScalarValueJsonConverter<SmokeQuantity, int>();
        (string Json, string Code)[] failures =
        [
            ("null", ValidationCodes.ValueNotNull),
            ("\" \"", ValidationCodes.ValueNotEmpty),
            ("\"invalid\"", ValidationCodes.FormatInteger),
            ("2147483648", ValidationCodes.FormatInteger),
            ("0", "smoke.quantity-positive"),
        ];

        foreach (var (json, code) in failures)
        {
            CheckFailure(generated, json, code);
            CheckFailure(runtime, json, code);
            if (json != "null")
                CheckFailure(optional, json, code);
        }

        using (ValidationErrorsContext.BeginScope())
        {
            Require(Read(optional, "null").HasNoValue, "Optional null must produce None.");
            Require(!ValidationErrorsContext.HasErrors, "Optional null must not record a violation.");
        }

        CheckRoundTrip(generated);
        CheckRoundTrip(runtime);
        Console.WriteLine("Scalar converter smoke passed (generated, runtime, optional; codes, args, body locations).");
    }

    private static void CheckFailure<T>(JsonConverter<T> converter, string json, string code)
    {
        using var scope = ValidationErrorsContext.BeginScope();
        ValidationErrorsContext.CurrentPropertyName = "/items/0/quantity";
        _ = Read(converter, json);
        var error = ValidationErrorsContext.GetUnprocessableContent();
        Require(error is not null && error.Fields.Length == 1, "Expected exactly one field violation.");
        var field = error!.Fields[0];
        Require(field.ReasonCode == code, $"Wrong reason code for {json}: {field.ReasonCode}.");
        Require(field.Field == InputPointer.ForBody("/items/0/quantity"), "Body location was lost.");
        if (json == "0")
            Require(field.Args is not null && field.Args["min"] == new ValidationArgValue.Number(1), "Rule args were lost.");
    }

    private static void CheckRoundTrip<T>(JsonConverter<T> converter)
    {
        using var scope = ValidationErrorsContext.BeginScope();
        var value = Read(converter, "3");
        Require(value is SmokeQuantity { Value: 3 }, "A valid scalar must deserialize.");
        Require(!ValidationErrorsContext.HasErrors, "A valid scalar must not record a violation.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            converter.Write(writer, value!, Options);
        Require(Encoding.UTF8.GetString(stream.ToArray()) == "3", "A valid scalar must serialize as its primitive.");
    }

    private static T? Read<T>(JsonConverter<T> converter, string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        Require(reader.Read(), "Expected a JSON token.");
        return converter.Read(ref reader, typeof(T), Options);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

internal sealed class SmokeQuantity : ScalarValueObject<SmokeQuantity, int>, IScalarValue<SmokeQuantity, int>
{
    private SmokeQuantity(int value) : base(value) { }

    public static Result<SmokeQuantity> TryCreate(int value, string? fieldName = null) =>
        value > 0
            ? Result.Ok(new SmokeQuantity(value))
            : Result.Fail<SmokeQuantity>(Error.InvalidInput.ForField(
                fieldName ?? "quantity", "smoke.quantity-positive", ValidationArgs.Of("min", 1), "Quantity must be positive."));

    public static Result<SmokeQuantity> TryCreate(string? value, string? fieldName = null) =>
        throw new NotSupportedException("The smoke probe exercises primitive JSON reads only.");
}
