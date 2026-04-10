namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

public class WrongAttributeNamespaceAnalyzerTests
{
    private const string StubSource = """
        using System;

        namespace System.ComponentModel.DataAnnotations
        {
            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
            public class StringLengthAttribute : Attribute
            {
                public StringLengthAttribute(int maximumLength) { }
            }

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
            public class RangeAttribute : Attribute
            {
                public RangeAttribute(int minimum, int maximum) { }
                public RangeAttribute(double minimum, double maximum) { }
            }
        }

        namespace Trellis
        {
            using System;

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
            public class StringLengthAttribute : Attribute
            {
                public StringLengthAttribute(int maximumLength) { }
            }

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
            public class RangeAttribute : Attribute
            {
                public RangeAttribute(int minimum, int maximum) { }
                public RangeAttribute(double minimum, double maximum) { }
            }

            public abstract class ScalarValueObject<TSelf, TPrimitive>
                where TSelf : ScalarValueObject<TSelf, TPrimitive>
                where TPrimitive : IComparable
            {
                public TPrimitive Value { get; protected set; } = default!;
            }

            public abstract class RequiredString<TSelf> : ScalarValueObject<TSelf, string>
                where TSelf : RequiredString<TSelf> { }

            public abstract class RequiredInt<TSelf> : ScalarValueObject<TSelf, int>
                where TSelf : RequiredInt<TSelf> { }

            public abstract class RequiredDecimal<TSelf> : ScalarValueObject<TSelf, decimal>
                where TSelf : RequiredDecimal<TSelf> { }

            public abstract class RequiredLong<TSelf> : ScalarValueObject<TSelf, long>
                where TSelf : RequiredLong<TSelf> { }

            public abstract class ValueObject { }
        }
        """;

    #region StringLength — wrong namespace on RequiredString

    [Fact]
    public async Task StringLength_DataAnnotations_OnRequiredString_ReportsDiagnostic()
    {
        const string source = """
            using Trellis;

            namespace TestNamespace
            {
                [System.ComponentModel.DataAnnotations.StringLength(100)]
                public partial class ProductName : RequiredString<ProductName> { }
            }
            """;

        var test = CreateTest(source,
            new DiagnosticResult(DiagnosticDescriptors.WrongAttributeNamespace)
                .WithLocation(5, 6)
                .WithArguments("ProductName", "StringLength"));

        await test.RunAsync();
    }

    #endregion

    #region Range — wrong namespace on RequiredInt

    [Fact]
    public async Task Range_DataAnnotations_OnRequiredInt_ReportsDiagnostic()
    {
        const string source = """
            using Trellis;

            namespace TestNamespace
            {
                [System.ComponentModel.DataAnnotations.Range(1, 1000)]
                public partial class Quantity : RequiredInt<Quantity> { }
            }
            """;

        var test = CreateTest(source,
            new DiagnosticResult(DiagnosticDescriptors.WrongAttributeNamespace)
                .WithLocation(5, 6)
                .WithArguments("Quantity", "Range"));

        await test.RunAsync();
    }

    #endregion

    #region Range — wrong namespace on RequiredDecimal

    [Fact]
    public async Task Range_DataAnnotations_OnRequiredDecimal_ReportsDiagnostic()
    {
        const string source = """
            using Trellis;

            namespace TestNamespace
            {
                [System.ComponentModel.DataAnnotations.Range(0.0, 100.0)]
                public partial class Percentage : RequiredDecimal<Percentage> { }
            }
            """;

        var test = CreateTest(source,
            new DiagnosticResult(DiagnosticDescriptors.WrongAttributeNamespace)
                .WithLocation(5, 6)
                .WithArguments("Percentage", "Range"));

        await test.RunAsync();
    }

    #endregion

    #region Trellis attributes — no diagnostic

    [Fact]
    public async Task StringLength_TrellisNamespace_NoDiagnostic()
    {
        const string source = """
            using Trellis;

            namespace TestNamespace
            {
                [StringLength(100)]
                public partial class ProductName : RequiredString<ProductName> { }
            }
            """;

        var test = CreateNoDiagnosticTest(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Range_TrellisNamespace_NoDiagnostic()
    {
        const string source = """
            using Trellis;

            namespace TestNamespace
            {
                [Range(1, 1000)]
                public partial class Quantity : RequiredInt<Quantity> { }
            }
            """;

        var test = CreateNoDiagnosticTest(source);
        await test.RunAsync();
    }

    #endregion

    #region Non-Trellis type — no diagnostic

    [Fact]
    public async Task StringLength_DataAnnotations_OnNonTrellisType_NoDiagnostic()
    {
        const string source = """
            namespace TestNamespace
            {
                [System.ComponentModel.DataAnnotations.StringLength(100)]
                public class RegularDto { }
            }
            """;

        var test = CreateNoDiagnosticTest(source);
        await test.RunAsync();
    }

    #endregion

    #region Composite ValueObject — no diagnostic (not a scalar base)

    [Fact]
    public async Task Range_DataAnnotations_OnValueObject_NoDiagnostic()
    {
        const string source = """
            using Trellis;

            namespace TestNamespace
            {
                [System.ComponentModel.DataAnnotations.Range(1, 100)]
                public class Money : ValueObject { }
            }
            """;

        var test = CreateNoDiagnosticTest(source);
        await test.RunAsync();
    }

    #endregion

    #region Test helpers

    private static CSharpAnalyzerTest<WrongAttributeNamespaceAnalyzer, DefaultVerifier> CreateTest(
        string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<WrongAttributeNamespaceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(("Stubs.cs", StubSource));
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private static CSharpAnalyzerTest<WrongAttributeNamespaceAnalyzer, DefaultVerifier> CreateNoDiagnosticTest(
        string source)
    {
        var test = new CSharpAnalyzerTest<WrongAttributeNamespaceAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(("Stubs.cs", StubSource));
        return test;
    }

    #endregion
}