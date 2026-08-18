// Cookbook Recipe 28 — Synthesise ProblemDetails.Instance from a ResourceRef.
namespace CookbookSnippets.Recipe28;

using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

// Aggregates with default plural names need no extra wiring. The naive default is
// type.ToLowerInvariant() + "s" — fine for "Order" → "orders", "Customer" → "customers".
public sealed class Customer
{
    public string Id { get; init; } = string.Empty;
}

// Override irregular plurals or domain-specific naming with the attribute (preferred —
// keeps the mapping next to the type).
[ResourceCollectionName("people")]
public sealed class Person
{
    public string Id { get; init; } = string.Empty;
}

[ResourceCollectionName("statuses")]
public sealed class Status
{
    public string Id { get; init; } = string.Empty;
}

public static class ResourceCollectionNameWiring
{
    // Composition root. AddTrellisAsp wires the registry; the typed extension registers a
    // single override and is AOT/trim-safe. Use the assembly scanner when you want every
    // [ResourceCollectionName]-tagged type in an assembly picked up at once.
    public static IServiceCollection Wire(IServiceCollection services)
    {
        services.AddTrellisAsp();
        services.AddResourceCollectionName<Person>("people");
        services.AddResourceCollectionName("LegacyDocument", "legacy-documents");
        services.AddResourceCollectionNames(typeof(Person).Assembly);  // alternative
        return services;
    }

    // POST /api/orders returning this error emits instance "/customers/abc-123" (Customer uses
    // the naive default plural) while the original request URI is preserved under
    // Extensions["request"].
    public static Error MissingCustomer() =>
        new Error.NotFound(ResourceRef.For<Customer>("abc-123"));
}