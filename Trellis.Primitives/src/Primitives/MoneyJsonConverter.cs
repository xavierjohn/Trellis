namespace Trellis.Primitives;

using System.Text.Json;
using System.Text.Json.Serialization;
using Trellis;

/// <summary>
/// JSON converter for the structured <see cref="Money"/> value object.
/// Serializes Money as {"amount": 99.99, "currency": "USD"}.
/// </summary>
public class MoneyJsonConverter : JsonConverter<Money>
{
    /// <summary>
    /// Reads Money from JSON.
    /// </summary>
    public override Money? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject token");

        decimal amount = 0;
        string? currency = null;
        bool amountFound = false;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var propertyName = reader.GetString();
                reader.Read();

                switch (propertyName?.ToLowerInvariant())
                {
                    case "amount":
                        amount = reader.GetDecimal();
                        amountFound = true;
                        break;
                    case "currency":
                        currency = reader.GetString();
                        break;
                }
            }
        }

        if (!amountFound)
            throw new JsonException("Required property 'amount' is missing.");

        if (currency == null)
            throw new JsonException("Currency is required.");

        var result = Money.TryCreate(amount, currency);
        if (result.IsFailure)
        {
            var error = result.Error;
            throw new JsonException(error.Detail);
        }

        return result.Value;
    }

    /// <summary>
    /// Writes Money to JSON.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("amount", value.Amount);
        writer.WriteString("currency", value.Currency);
        writer.WriteEndObject();
    }
}