namespace Trellis.Analyzers.Tests;

using Xunit;

/// <summary>
/// Tests for RedundantEfConfigurationAnalyzer (TRLS021).
/// </summary>
public class RedundantEfConfigurationAnalyzerTests
{
    private const string EfCoreBuilderStubSource = """
        namespace Microsoft.EntityFrameworkCore
        {
            public class ModelConfigurationBuilder
            {
            }
        }

        namespace Microsoft.EntityFrameworkCore.Metadata.Builders
        {
            using System;
            using System.Linq.Expressions;

            public class EntityTypeBuilder<TEntity> where TEntity : class
            {
                public virtual PropertyBuilder<TProperty> Property<TProperty>(Expression<Func<TEntity, TProperty>> propertyExpression)
                    => new PropertyBuilder<TProperty>();

                public virtual OwnedNavigationBuilder<TEntity, TRelatedEntity> OwnsOne<TRelatedEntity>(
                    Expression<Func<TEntity, TRelatedEntity>> navigationExpression)
                    where TRelatedEntity : class
                    => new OwnedNavigationBuilder<TEntity, TRelatedEntity>();

                public virtual EntityTypeBuilder<TEntity> Ignore(Expression<Func<TEntity, object?>> propertyExpression)
                    => this;

                public virtual EntityTypeBuilder<TEntity> Ignore(string propertyName)
                    => this;
            }

            public class PropertyBuilder<TProperty>
            {
                public virtual PropertyBuilder<TProperty> HasConversion<TProvider>()
                    => this;

                public virtual PropertyBuilder<TProperty> HasMaxLength(int maxLength)
                    => this;
            }

            public class OwnedNavigationBuilder<TEntity, TRelatedEntity>
                where TEntity : class
                where TRelatedEntity : class
            {
            }
        }

        namespace Trellis.EntityFrameworkCore
        {
            using System;
            using Microsoft.EntityFrameworkCore;

            public class MaybeConvention
            {
            }

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
            public sealed class OwnedEntityAttribute : Attribute
            {
            }

            public static class ModelConfigurationBuilderExtensions
            {
                public static ModelConfigurationBuilder ApplyTrellisConventions(
                    this ModelConfigurationBuilder configurationBuilder,
                    params System.Reflection.Assembly[] assemblies)
                    => configurationBuilder;

                public static ModelConfigurationBuilder ApplyTrellisConventionsFor<TContext>(
                    this ModelConfigurationBuilder configurationBuilder)
                    => configurationBuilder;
            }
        }
        """;

    [Fact]
    public async Task HasConversion_OnMaybeProperty_WithTrellisConventions_ProducesWarning()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Trellis.EntityFrameworkCore;

            public class Order
            {
                public int Id { get; set; }
                public Maybe<string> PhoneNumber { get; set; }
            }

            public class OrderConfig
            {
                public void Configure(EntityTypeBuilder<Order> builder)
                {
                    builder.Property(e => e.PhoneNumber).HasConversion<string>();
                }
            }

            public class AppDbContext
            {
                protected void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
                {
                    configurationBuilder.ApplyTrellisConventions(typeof(Order).Assembly);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<RedundantEfConfigurationAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.RedundantEfConfiguration)
                .WithLocation(21, 46)
                .WithArguments("HasConversion", "Order.PhoneNumber"));
        test.TestState.Sources.Add(("EfCoreBuilderStubs.cs", EfCoreBuilderStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task OwnsOne_OnOwnedEntityProperty_WithTrellisConventionsFor_ProducesWarning()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Trellis.EntityFrameworkCore;

            public class Order
            {
                public int Id { get; set; }
                public Money Total { get; set; } = null!;
            }

            [OwnedEntity]
            public class Money
            {
                public decimal Amount { get; set; }
            }

            public class OrderConfig
            {
                public void Configure(EntityTypeBuilder<Order> builder)
                {
                    builder.OwnsOne(e => e.Total);
                }
            }

            public class AppDbContext
            {
                protected void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
                {
                    configurationBuilder.ApplyTrellisConventionsFor<AppDbContext>();
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<RedundantEfConfigurationAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.RedundantEfConfiguration)
                .WithLocation(27, 17)
                .WithArguments("OwnsOne", "Order.Total"));
        test.TestState.Sources.Add(("EfCoreBuilderStubs.cs", EfCoreBuilderStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task Ignore_OnMaybeProperty_WithTrellisConventions_ProducesWarning()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Trellis.EntityFrameworkCore;

            public class Order
            {
                public int Id { get; set; }
                public Maybe<string> PhoneNumber { get; set; }
            }

            public class OrderConfig
            {
                public void Configure(EntityTypeBuilder<Order> builder)
                {
                    builder.Ignore(e => e.PhoneNumber);
                }
            }

            public class AppDbContext
            {
                protected void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
                {
                    configurationBuilder.ApplyTrellisConventions();
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<RedundantEfConfigurationAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.RedundantEfConfiguration)
                .WithLocation(21, 17)
                .WithArguments("Ignore", "Order.PhoneNumber"));
        test.TestState.Sources.Add(("EfCoreBuilderStubs.cs", EfCoreBuilderStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task HasConversion_OnMaybeProperty_WithoutTrellisConventions_NoDiagnostic()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore.Metadata.Builders;

            public class Order
            {
                public int Id { get; set; }
                public Maybe<string> PhoneNumber { get; set; }
            }

            public class OrderConfig
            {
                public void Configure(EntityTypeBuilder<Order> builder)
                {
                    builder.Property(e => e.PhoneNumber).HasConversion<string>();
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<RedundantEfConfigurationAnalyzer>(source);
        test.TestState.Sources.Add(("EfCoreBuilderStubs.cs", EfCoreBuilderStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task HasConversion_OnMaybeProperty_WithUnrelatedApplyTrellisConventionsName_NoDiagnostic()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using MyCompany.EntityFrameworkCore;

            namespace MyCompany.EntityFrameworkCore
            {
                public static class ModelConfigurationBuilderExtensions
                {
                    public static ModelConfigurationBuilder ApplyTrellisConventions(
                        this ModelConfigurationBuilder configurationBuilder)
                        => configurationBuilder;
                }
            }

            public class Order
            {
                public int Id { get; set; }
                public Maybe<string> PhoneNumber { get; set; }
            }

            public class OrderConfig
            {
                public void Configure(EntityTypeBuilder<Order> builder)
                {
                    builder.Property(e => e.PhoneNumber).HasConversion<string>();
                }
            }

            public class AppDbContext
            {
                protected void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
                {
                    configurationBuilder.ApplyTrellisConventions();
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<RedundantEfConfigurationAnalyzer>(source);
        test.TestState.Sources.Add(("EfCoreBuilderStubs.cs", EfCoreBuilderStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task ChainedHasConversion_OnMaybeProperty_WithTrellisConventions_ProducesWarning()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Trellis.EntityFrameworkCore;

            public class Order
            {
                public int Id { get; set; }
                public Maybe<string> PhoneNumber { get; set; }
            }

            public class OrderConfig
            {
                public void Configure(EntityTypeBuilder<Order> builder)
                {
                    builder.Property(e => e.PhoneNumber).HasMaxLength(20).HasConversion<string>();
                }
            }

            public class AppDbContext
            {
                protected void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
                {
                    configurationBuilder.ApplyTrellisConventions();
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<RedundantEfConfigurationAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.RedundantEfConfiguration)
                .WithLocation(21, 63)
                .WithArguments("HasConversion", "Order.PhoneNumber"));
        test.TestState.Sources.Add(("EfCoreBuilderStubs.cs", EfCoreBuilderStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task Ignore_StringOverload_NoDiagnostic()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Trellis.EntityFrameworkCore;

            public class Order
            {
                public int Id { get; set; }
                public Maybe<string> PhoneNumber { get; set; }
            }

            public class OrderConfig
            {
                public void Configure(EntityTypeBuilder<Order> builder)
                {
                    builder.Ignore("PhoneNumber");
                }
            }

            public class AppDbContext
            {
                protected void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
                {
                    configurationBuilder.ApplyTrellisConventions();
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<RedundantEfConfigurationAnalyzer>(source);
        test.TestState.Sources.Add(("EfCoreBuilderStubs.cs", EfCoreBuilderStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task HasConversion_OnRegularProperty_WithTrellisConventions_NoDiagnostic()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Trellis.EntityFrameworkCore;

            public class Order
            {
                public int Id { get; set; }
                public string Status { get; set; } = "";
            }

            public class OrderConfig
            {
                public void Configure(EntityTypeBuilder<Order> builder)
                {
                    builder.Property(e => e.Status).HasConversion<string>();
                }
            }

            public class AppDbContext
            {
                protected void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
                {
                    configurationBuilder.ApplyTrellisConventions();
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<RedundantEfConfigurationAnalyzer>(source);
        test.TestState.Sources.Add(("EfCoreBuilderStubs.cs", EfCoreBuilderStubSource));

        await test.RunAsync();
    }

    [Fact]
    public async Task UserDefinedApplyTrellisConventions_DoesNotEnableAnalyzer()
    {
        // A user-defined extension method with the same name as the Trellis convention
        // wiring methods must NOT enable TRLS021.  The analyzer must resolve the symbol
        // and confirm it belongs to a Trellis-owned containing type before firing.
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Trellis.EntityFrameworkCore;
            using MyCompany.Data;

            public class Order
            {
                public int Id { get; set; }
                public Maybe<string> PhoneNumber { get; set; }
            }

            public class OrderConfig
            {
                public void Configure(EntityTypeBuilder<Order> builder)
                {
                    builder.Ignore(e => e.PhoneNumber);
                }
            }

            public class AppDbContext
            {
                protected void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
                {
                    // Calls user-defined ApplyTrellisConventions — NOT the Trellis extension.
                    configurationBuilder.ApplyTrellisConventions();
                }
            }
            """;

        // User-defined extension that lives in a different containing type.
        const string userConventionsStub = """
            namespace MyCompany.Data
            {
                using Microsoft.EntityFrameworkCore;

                public static class MyConventions
                {
                    public static ModelConfigurationBuilder ApplyTrellisConventions(
                        this ModelConfigurationBuilder configurationBuilder)
                        => configurationBuilder;
                }
            }
            """;

        // The stub source here omits ModelConfigurationBuilderExtensions so the only
        // ApplyTrellisConventions in scope is the user-defined one.
        const string efStubWithoutTrellisExtension = """
            namespace Microsoft.EntityFrameworkCore
            {
                public class ModelConfigurationBuilder
                {
                }
            }

            namespace Microsoft.EntityFrameworkCore.Metadata.Builders
            {
                using System;
                using System.Linq.Expressions;

                public class EntityTypeBuilder<TEntity> where TEntity : class
                {
                    public virtual PropertyBuilder<TProperty> Property<TProperty>(Expression<Func<TEntity, TProperty>> propertyExpression)
                        => new PropertyBuilder<TProperty>();

                    public virtual OwnedNavigationBuilder<TEntity, TRelatedEntity> OwnsOne<TRelatedEntity>(
                        Expression<Func<TEntity, TRelatedEntity>> navigationExpression)
                        where TRelatedEntity : class
                        => new OwnedNavigationBuilder<TEntity, TRelatedEntity>();

                    public virtual EntityTypeBuilder<TEntity> Ignore(Expression<Func<TEntity, object?>> propertyExpression)
                        => this;

                    public virtual EntityTypeBuilder<TEntity> Ignore(string propertyName)
                        => this;
                }

                public class PropertyBuilder<TProperty>
                {
                    public virtual PropertyBuilder<TProperty> HasConversion<TProvider>()
                        => this;

                    public virtual PropertyBuilder<TProperty> HasMaxLength(int maxLength)
                        => this;
                }

                public class OwnedNavigationBuilder<TEntity, TRelatedEntity>
                    where TEntity : class
                    where TRelatedEntity : class
                {
                }
            }

            namespace Trellis.EntityFrameworkCore
            {
                using System;
                using Microsoft.EntityFrameworkCore;

                public class MaybeConvention
                {
                }

                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
                public sealed class OwnedEntityAttribute : Attribute
                {
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<RedundantEfConfigurationAnalyzer>(source);
        test.TestState.Sources.Add(("EfStubWithoutTrellisExtension.cs", efStubWithoutTrellisExtension));
        test.TestState.Sources.Add(("UserConventionsStub.cs", userConventionsStub));

        await test.RunAsync();
    }

    [Fact]
    public async Task TrellisAndUserDefinedApplyTrellisConventions_TrellisCallEnablesAnalyzer()
    {
        // When both the Trellis-owned extension and a user-defined method with the same name
        // exist in scope, and the TRELLIS-owned overload is called, TRLS021 must fire.
        // This confirms the symbol-resolution correctly distinguishes between the two.
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Trellis.EntityFrameworkCore;
            using MyCompany.Data;

            public class Order
            {
                public int Id { get; set; }
                public Maybe<string> PhoneNumber { get; set; }
            }

            public class OrderConfig
            {
                public void Configure(EntityTypeBuilder<Order> builder)
                {
                    builder.Ignore(e => e.PhoneNumber);
                }
            }

            public class AppDbContext
            {
                protected void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
                {
                    // Calls the TRELLIS-owned overload (takes assemblies); user method takes none.
                    configurationBuilder.ApplyTrellisConventions(typeof(Order).Assembly);
                }
            }
            """;

        // User-defined extension has a different signature (no args) to avoid CS0121 ambiguity.
        // The call above is unambiguously resolved to the Trellis-owned overload.
        const string userConventionsStub = """
            namespace MyCompany.Data
            {
                using Microsoft.EntityFrameworkCore;

                public static class MyConventions
                {
                    public static ModelConfigurationBuilder ApplyTrellisConventions(
                        this ModelConfigurationBuilder configurationBuilder)
                        => configurationBuilder;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<RedundantEfConfigurationAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.RedundantEfConfiguration)
                .WithLocation(22, 17)
                .WithArguments("Ignore", "Order.PhoneNumber"));
        test.TestState.Sources.Add(("EfCoreBuilderStubs.cs", EfCoreBuilderStubSource));
        test.TestState.Sources.Add(("UserConventionsStub.cs", userConventionsStub));

        await test.RunAsync();
    }
}
