namespace Trellis.AotConsoleProbe;

using System.Text.Json;
using System.Text.Json.Serialization;
using Trellis;
using Trellis.Primitives;

/// <summary>
/// A scalar value object declared in the same compilation as the serializer context.
/// System.Text.Json's generator only observes attributes present in original source, so the
/// converter is declared here rather than left to the Trellis generator.
/// </summary>
[JsonConverter(typeof(ParsableJsonConverter<ProbeOrderId>))]
public partial class ProbeOrderId : RequiredGuid<ProbeOrderId>
{
}

public sealed class ProbeOrder
{
    public ProbeOrderId? Id { get; set; }

    public Money? Total { get; set; }
}

[JsonSerializable(typeof(ProbeOrder))]
[JsonSerializable(typeof(ProbeOrderId))]
public partial class ProbeJsonContext : JsonSerializerContext
{
}

public static class Program
{
    /// <summary>
    /// The composite converter is allowed to fail under AOT, but its message must still name the
    /// cause and the way out. These terms are the difference between a usable error and the one
    /// that previously sent readers to debug a correct <c>TryCreate</c>.
    /// </summary>
    private static readonly string[] RequiredDiagnosisTerms = ["trimmed", "Native AOT", "JsonConverter"];

    public static int Main()
    {
        var failures = new List<string>();

        Check(failures, "scalar value object round-trips", static () =>
        {
            var id = ProbeOrderId.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
            var json = JsonSerializer.Serialize(id, ProbeJsonContext.Default.ProbeOrderId);
            if (json != "\"11111111-1111-1111-1111-111111111111\"")
                throw new InvalidOperationException($"expected a bare scalar, got {json}");

            var restored = JsonSerializer.Deserialize(json, ProbeJsonContext.Default.ProbeOrderId);
            if (restored is null || restored.Value != id.Value)
                throw new InvalidOperationException($"round-trip lost the value: {restored?.Value}");
        });

        Check(failures, "scalar validation still rejects bad input", static () =>
        {
            var result = ProbeOrderId.TryCreate((Guid?)null);
            if (result.IsSuccess)
                throw new InvalidOperationException("null should not produce a valid value object");
        });

        Check(failures, "composite value object behaves predictably", static () =>
        {
            var money = Money.Create(19.99m, "USD");
            var order = new ProbeOrder { Id = ProbeOrderId.Create(Guid.NewGuid()), Total = money };

            string json;
            try
            {
                json = JsonSerializer.Serialize(order, ProbeJsonContext.Default.ProbeOrder);
            }
            catch (Exception ex)
            {
                // A composite value object relies on reflection the trimmer may remove. Failing is
                // acceptable; failing without explaining why is not. The earlier regression threw an
                // InvalidOperationException blaming a TryCreate overload that was in fact correct,
                // sending readers to debug the wrong thing. Assert the diagnosis and the remedy.
                Report("composite serialize threw", ex);

                foreach (var expected in RequiredDiagnosisTerms)
                {
                    if (!ex.Message.Contains(expected, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            $"composite failure message must explain the cause and remedy; missing '{expected}' in: {ex.Message}");
                }

                return;
            }

            Console.WriteLine($"  composite serialized: {json}");

            var restored = JsonSerializer.Deserialize(json, ProbeJsonContext.Default.ProbeOrder);
            if (restored?.Total is null)
                throw new InvalidOperationException("composite round-trip lost Total");
            if (restored.Total.Amount != money.Amount || restored.Total.Currency != money.Currency)
                throw new InvalidOperationException(
                    $"composite round-trip changed the value: {restored.Total.Amount} {restored.Total.Currency}");
        });

        if (failures.Count == 0)
        {
            Console.WriteLine("AOT console probe: PASS");
            return 0;
        }

        Console.WriteLine("AOT console probe: FAIL");
        foreach (var failure in failures)
            Console.WriteLine($"  - {failure}");

        return 1;
    }

    private static void Check(List<string> failures, string description, Action assertion)
    {
        try
        {
            assertion();
            Console.WriteLine($"[ok]   {description}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {description}: {ex.GetType().Name}: {ex.Message}");
            failures.Add($"{description}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Report(string label, Exception ex) =>
        Console.WriteLine($"  {label}: {ex.GetType().Name}: {ex.Message}");
}