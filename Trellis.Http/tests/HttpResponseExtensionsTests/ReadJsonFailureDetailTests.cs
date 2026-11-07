namespace Trellis.Http.Tests.HttpResponseExtensionsTests;

using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Testing;

/// <summary>
/// A malformed body must produce a diagnostic that carries only the target type and the structured
/// line/byte position. The exception message echoes payload text and the exception path carries
/// dictionary keys chosen by the remote party, so neither may reach the public detail.
/// </summary>
public partial class ReadJsonFailureDetailTests
{
    private const string CustomerKey = "alice@example.com";
    private const string CustomerValue = "not-a-number-12345";
    private const string ConverterSecret = "converter-secret-67890";

    private sealed record Body(string Name, string Json, string ExpectedLocation);

    // Positions are those System.Text.Json reports: zero-based line and byte offset where the reader stopped.
    private static readonly Body[] s_bodies =
    [
        new("value-of-key-has-wrong-type, single line",
            $$$"""{"customers":{"{{{CustomerKey}}}":"{{{CustomerValue}}}"}}""",
            " at line 0, byte 54"),
        new("value-of-key-has-wrong-type, multiple lines",
            $$$"""
            {
              "customers": {
                "{{{CustomerKey}}}": "{{{CustomerValue}}}"
              }
            }
            """,
            " at line 2, byte 45"),
        new("syntax-error-after-key",
            $$$"""{"customers":{"{{{CustomerKey}}}":}}""",
            " at line 0, byte 34"),
        new("converter-throws-with-secret-message",
            """{"ledger":"x"}""",
            " at line 0, byte 13"),
    ];

    public static TheoryData<string, string> BodyNames()
    {
        var data = new TheoryData<string, string>();
        foreach (var reader in Readers.Keys)
            foreach (var body in s_bodies)
                data.Add(reader, body.Name);
        return data;
    }

    private static readonly Dictionary<string, Func<HttpResponseMessage, Task<Result<object>>>> Readers = new()
    {
        ["ReadJsonAsync"] = async message =>
            (await Task.FromResult(Result.Ok(message)).ReadJsonAsync(LedgerContext.Default.Ledger, CancellationToken.None))
                .Map(v => (object)v),
        ["ReadJsonMaybeAsync"] = async message =>
            (await Task.FromResult(Result.Ok(message)).ReadJsonMaybeAsync(LedgerContext.Default.Ledger, CancellationToken.None))
                .Map(v => (object)v),
        ["ReadJsonOrNoneOn404Async"] = async message =>
            (await Task.FromResult(message).ReadJsonOrNoneOn404Async(LedgerContext.Default.Ledger, CancellationToken.None))
                .Map(v => (object)v),
    };

    [Theory]
    [MemberData(nameof(BodyNames))]
    public async Task Invalid_body_detail_carries_only_type_and_structured_position(string reader, string bodyName)
    {
        var body = Array.Find(s_bodies, b => b.Name == bodyName)!;
        var tracker = new TrackingHttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body.Json),
        };

        var result = await Readers[reader](tracker);

        var error = result.Should().BeFailureOfType<Error.Unexpected>().Subject;
        error.Code.Should().Be(FaultCodes.HttpResponseInvalidBody);
        error.Detail.Should().Be($"Failed to deserialize HTTP response to Ledger{body.ExpectedLocation}.");
        tracker.Disposed.Should().BeTrue();
    }

    public sealed class Ledger
    {
        [JsonPropertyName("customers")]
        public Dictionary<string, int> Customers { get; set; } = [];

        [JsonPropertyName("ledger")]
        [JsonConverter(typeof(ThrowingConverter))]
        public string? Entry { get; set; }
    }

    public sealed class ThrowingConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new JsonException(ConverterSecret);

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }

    [JsonSerializable(typeof(Ledger))]
    internal sealed partial class LedgerContext : JsonSerializerContext
    {
    }
}
