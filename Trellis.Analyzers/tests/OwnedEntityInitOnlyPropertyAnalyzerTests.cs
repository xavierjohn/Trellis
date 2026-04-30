namespace Trellis.Analyzers.Tests;

using Xunit;

/// <summary>
/// Tests for <see cref="OwnedEntityInitOnlyPropertyAnalyzer"/> (TRLS022 — init-only properties on
/// <c>[OwnedEntity]</c> types).
/// </summary>
public class OwnedEntityInitOnlyPropertyAnalyzerTests
{
    [Fact]
    public async Task OwnedEntity_PropertyWithInitSetter_ReportsDiagnostic()
    {
        const string source = """
            using Trellis.EntityFrameworkCore;

            [OwnedEntity]
            public partial class Address
            {
                public string Street { get; init; } = "";
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<OwnedEntityInitOnlyPropertyAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.OwnedEntityInitOnlyProperty)
                .WithArguments("Street", "Address")
                .WithLocation(12, 33));
        test.TestState.Sources.Add(("OwnedEntityStubs.cs", OwnedEntityTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task OwnedEntity_PropertyWithPrivateSet_NoDiagnostic()
    {
        const string source = """
            using Trellis.EntityFrameworkCore;

            [OwnedEntity]
            public partial class Address
            {
                public string Street { get; private set; } = "";
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<OwnedEntityInitOnlyPropertyAnalyzer>(source);
        test.TestState.Sources.Add(("OwnedEntityStubs.cs", OwnedEntityTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task OwnedEntity_PropertyWithPublicSet_NoDiagnostic()
    {
        const string source = """
            using Trellis.EntityFrameworkCore;

            [OwnedEntity]
            public partial class Address
            {
                public string Street { get; set; } = "";
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<OwnedEntityInitOnlyPropertyAnalyzer>(source);
        test.TestState.Sources.Add(("OwnedEntityStubs.cs", OwnedEntityTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task OwnedEntity_GetOnlyProperty_NoDiagnostic()
    {
        const string source = """
            using Trellis.EntityFrameworkCore;

            [OwnedEntity]
            public partial class Address
            {
                public string Street { get; } = "";
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<OwnedEntityInitOnlyPropertyAnalyzer>(source);
        test.TestState.Sources.Add(("OwnedEntityStubs.cs", OwnedEntityTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task NonOwnedEntity_PropertyWithInitSetter_NoDiagnostic()
    {
        const string source = """
            public class RegularClass
            {
                public string Name { get; init; } = "";
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<OwnedEntityInitOnlyPropertyAnalyzer>(source);
        test.TestState.Sources.Add(("OwnedEntityStubs.cs", OwnedEntityTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task UnrelatedAttributeNamedOwnedEntity_InitProperty_NoDiagnostic()
    {
        const string source = """
            using System;

            namespace OtherLibrary
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class OwnedEntityAttribute : Attribute;
            }

            namespace Test
            {
                [OtherLibrary.OwnedEntity]
                public class Address
                {
                    public string Street { get; init; } = "";
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<OwnedEntityInitOnlyPropertyAnalyzer>(source);
        test.TestState.Sources.Add(("OwnedEntityStubs.cs", OwnedEntityTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task OwnedEntity_MultipleInitProperties_ReportsAllDiagnostics()
    {
        const string source = """
            using Trellis.EntityFrameworkCore;

            [OwnedEntity]
            public partial class Address
            {
                public string Street { get; init; } = "";
                public string City { get; init; } = "";
                public string State { get; private set; } = "";
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<OwnedEntityInitOnlyPropertyAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.OwnedEntityInitOnlyProperty)
                .WithArguments("Street", "Address")
                .WithLocation(12, 33),
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.OwnedEntityInitOnlyProperty)
                .WithArguments("City", "Address")
                .WithLocation(13, 31));
        test.TestState.Sources.Add(("OwnedEntityStubs.cs", OwnedEntityTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task OwnedEntity_RecordWithInitProperty_ReportsDiagnostic()
    {
        const string source = """
            using Trellis.EntityFrameworkCore;

            [OwnedEntity]
            public partial record class Address
            {
                public string Street { get; init; } = "";
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<OwnedEntityInitOnlyPropertyAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.OwnedEntityInitOnlyProperty)
                .WithArguments("Street", "Address")
                .WithLocation(12, 33));
        test.TestState.Sources.Add(("OwnedEntityStubs.cs", OwnedEntityTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task OwnedEntity_InheritedInitPropertyFromNonOwnedBase_ReportsDiagnostic()
    {
        const string source = """
            using Trellis.EntityFrameworkCore;

            public class BaseAddress
            {
                public string Country { get; init; } = "";
            }

            [OwnedEntity]
            public partial class Address : BaseAddress
            {
                public string Street { get; private set; } = "";
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<OwnedEntityInitOnlyPropertyAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.OwnedEntityInitOnlyProperty)
                .WithArguments("Country", "Address")
                .WithLocation(11, 34));
        test.TestState.Sources.Add(("OwnedEntityStubs.cs", OwnedEntityTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task OwnedEntity_DerivedHidesBaseInitProperty_NoDiagnostic()
    {
        const string source = """
            using Trellis.EntityFrameworkCore;

            public class BaseAddress
            {
                public string Country { get; init; } = "";
            }

            [OwnedEntity]
            public partial class Address : BaseAddress
            {
                public new string Country { get; private set; } = "";
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<OwnedEntityInitOnlyPropertyAnalyzer>(source);
        test.TestState.Sources.Add(("OwnedEntityStubs.cs", OwnedEntityTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task OwnedEntity_PrivateInitProperty_NoDiagnostic()
    {
        const string source = """
            using Trellis.EntityFrameworkCore;

            [OwnedEntity]
            public partial class Address
            {
                private string Secret { get; init; } = "";
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<OwnedEntityInitOnlyPropertyAnalyzer>(source);
        test.TestState.Sources.Add(("OwnedEntityStubs.cs", OwnedEntityTestStubs.Source));

        await test.RunAsync();
    }
}
