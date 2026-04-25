// Cookbook Recipe 8 — EF Core: MaybePropertyMapping for nullable value objects.
namespace CookbookSnippets.Recipe08;

using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trellis;
using Trellis.EntityFrameworkCore;

public sealed partial class CustomerId : RequiredGuid<CustomerId>;

public sealed partial class EmailAddress : RequiredString<EmailAddress>;

public sealed partial class Customer : Aggregate<CustomerId>
{
    public Customer(CustomerId id) : base(id) { }

    public partial Maybe<EmailAddress> Email { get; set; }

    // Status is referenced by the FIX-1 HasTrellisIndex example.
    public string Status { get; set; } = string.Empty;
}

// Diagnostics — print the generated storage members for every Maybe<T> in the model.
public static class ModelDiagnostics
{
    public static void DumpMaybeMappings(DbContext db)
    {
        IReadOnlyList<MaybePropertyMapping> mappings = db.GetMaybePropertyMappings();
        foreach (var m in mappings)
            Console.WriteLine($"{m.EntityTypeName}.{m.PropertyName} → {m.MappedBackingFieldName} ({m.StoreType.Name})");
    }
}

#if FALSE
// WRONG — HasIndex against the CLR Maybe<T> property silently fails (TRLS016).
internal static class AntiPattern
{
    public static void Configure(EntityTypeBuilder<Customer> b) =>
        b.HasIndex(c => c.Email);
}
#endif

// FIX 1 — strongly-typed Trellis index helper.
// FIX 2 — string-based HasIndex against the storage member.
public static class FixPattern
{
    public static void Configure(EntityTypeBuilder<Customer> b)
    {
        b.HasTrellisIndex(c => new { c.Status, c.Email });
        b.HasIndex("Status", "_email");
    }
}
