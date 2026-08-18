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
internal static class Recipe8MaybeMappingSurface
{
    public static void InterceptorRegistrationSurface(DbContextOptionsBuilder<DbContext> typedBuilder, DbContextOptionsBuilder untypedBuilder)
    {
        DbContextOptionsBuilder<DbContext> typed = typedBuilder.AddTrellisInterceptors();
        DbContextOptionsBuilder untyped = untypedBuilder.AddTrellisInterceptors();
        Type interceptorType = typeof(MaybeQueryInterceptor);

        _ = (typed, untyped, interceptorType);
    }

    public static void MaybeExpressionShapes(EmailAddress fallback)
    {
        System.Linq.Expressions.Expression<Func<Customer, bool>> hasValue = c => c.Email.HasValue;
        System.Linq.Expressions.Expression<Func<Customer, EmailAddress>> value = c => c.Email.Value;
        System.Linq.Expressions.Expression<Func<Customer, EmailAddress>> defaulted = c => c.Email.GetValueOrDefault(fallback);
        System.Linq.Expressions.Expression<Func<Customer, bool>> noneComparison = c => c.Email == Maybe<EmailAddress>.None;

        _ = (hasValue, value, defaulted, noneComparison);
    }

    public static async Task SpecificationAndFakeRepository_QueryAsync(Customer customer, CancellationToken cancellationToken)
    {
        Specification<Customer> specification = new CustomersWithEmailSpecification();
        var repository = new Trellis.Testing.FakeRepository<Customer, CustomerId>();
        repository.Add(customer);

        IReadOnlyList<Customer> matches = await repository.QueryAsync(specification, cancellationToken);

        _ = matches;
    }

    public static void MaybeQueryableExtensionsSurface(IQueryable<Customer> customers, EmailAddress email)
    {
        IQueryable<Customer> hasValue = customers.WhereHasValue(c => c.Email);
        IQueryable<Customer> none = customers.WhereNone(c => c.Email);
        IQueryable<Customer> equals = customers.WhereEquals(c => c.Email, email);
        IQueryable<Customer> lessThan = customers.WhereLessThan(c => c.Email, email);
        IQueryable<Customer> greaterThanOrEqual = customers.WhereGreaterThanOrEqual(c => c.Email, email);
        IOrderedQueryable<Customer> ordered = customers.OrderByMaybe(c => c.Email);
        IOrderedQueryable<Customer> thenOrdered = ordered.ThenByMaybe(c => c.Email);

        _ = (hasValue, none, equals, lessThan, greaterThanOrEqual, ordered, thenOrdered);
    }

    private sealed class CustomersWithEmailSpecification : Specification<Customer>
    {
        public override System.Linq.Expressions.Expression<Func<Customer, bool>> ToExpression() =>
            customer => customer.Email.HasValue;
    }
}