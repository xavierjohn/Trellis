namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.Testing;
using Xunit;

/// <summary>
/// Tests for <see cref="ReasonCodeVocabularyCodeFixProvider"/> (TRLS064).
/// </summary>
public class ReasonCodeVocabularyCodeFixProviderTests
{
    private static async Task VerifyFixAsync(string source, string fixedSource, params DiagnosticResult[] expected)
    {
        var test = CodeFixTestHelper
            .CreateCodeFixTest<ReasonCodeVocabularyAnalyzer, ReasonCodeVocabularyCodeFixProvider>(
                source, fixedSource, expected);

        test.TestState.Sources.Add(("ReasonCodeStubs.cs", ReasonCodeTestStubs.Source));
        test.FixedState.Sources.Add(("ReasonCodeStubs.cs", ReasonCodeTestStubs.Source));

        await test.RunAsync();
    }

    private static DiagnosticResult Expect(int location, string code, string constant) =>
        CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.ReasonCodeVocabulary)
            .WithLocation(location)
            .WithArguments(
                code,
                $"is the framework reason code {constant}; emit it by constant, because a typo in a "
                + "literal is a silent wire break while a typo in a constant name does not compile");

    [Fact]
    public async Task Frozen_literal_is_replaced_by_its_constant() =>
        await VerifyFixAsync(
            """
            public class Codes
            {
                public void Emit() => Failure.ForField("name", {|#0:"value.not-null"|});
            }
            """,
            """
            public class Codes
            {
                public void Emit() => Failure.ForField("name", ValidationCodes.ValueNotNull);
            }
            """,
            Expect(0, "value.not-null", "ValidationCodes.ValueNotNull"));

    [Fact]
    public async Task Fault_code_literal_is_replaced_by_its_constant() =>
        await VerifyFixAsync(
            """
            public class Codes
            {
                public void Emit() => Failure.ForReason({|#0:"not-implemented"|});
            }
            """,
            """
            public class Codes
            {
                public void Emit() => Failure.ForReason(FaultCodes.NotImplemented);
            }
            """,
            Expect(0, "not-implemented", "FaultCodes.NotImplemented"));

    [Fact]
    public async Task All_occurrences_are_fixed_together() =>
        // The motivating case for fix-all: one literal repeated across many call sites.
        await VerifyFixAsync(
            """
            public class Codes
            {
                public void First() => Failure.ForRule({|#0:"value.not-null"|});
                public void Second() => Failure.ForRule({|#1:"value.not-null"|});
                public void Third() => Failure.ForField("x", {|#2:"value.not-null"|});
            }
            """,
            """
            public class Codes
            {
                public void First() => Failure.ForRule(ValidationCodes.ValueNotNull);
                public void Second() => Failure.ForRule(ValidationCodes.ValueNotNull);
                public void Third() => Failure.ForField("x", ValidationCodes.ValueNotNull);
            }
            """,
            Expect(0, "value.not-null", "ValidationCodes.ValueNotNull"),
            Expect(1, "value.not-null", "ValidationCodes.ValueNotNull"),
            Expect(2, "value.not-null", "ValidationCodes.ValueNotNull"));

    [Fact]
    public async Task Namespace_squatting_offers_no_fix()
    {
        // There is no mechanical replacement — only the author knows which namespace the code belongs
        // in — so the source is expected to come back unchanged.
        const string source = """
            public class Codes
            {
                public void Emit() => Failure.ForRule({|#0:"value.my-own-check"|});
            }
            """;

        await VerifyFixAsync(
            source,
            source,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.ReasonCodeVocabulary)
                .WithLocation(0)
                .WithArguments(
                    "value.my-own-check",
                    "claims the framework namespace 'value.*', whose meaning Trellis publishes, so a "
                    + "client falling back on the prefix will read this application code as a framework "
                    + "one — application codes are free-form, so pick a namespace the framework does not own"));
    }

    [Fact]
    public async Task WithErrorCode_literal_is_replaced_by_its_constant() =>
        await VerifyFixAsync(
            """
            using FluentValidation;

            public class Rules
            {
                public IRuleBuilderOptions<object, string> Builder = null!;

                public void Apply() => Builder.WithErrorCode({|#0:"value.not-null"|});
            }
            """,
            """
            using FluentValidation;

            public class Rules
            {
                public IRuleBuilderOptions<object, string> Builder = null!;

                public void Apply() => Builder.WithErrorCode(ValidationCodes.ValueNotNull);
            }
            """,
            Expect(0, "value.not-null", "ValidationCodes.ValueNotNull"));

    [Fact]
    public async Task Attribute_Code_literal_is_replaced_by_its_constant() =>
        // Attribute arguments must be compile-time constants, which a const string is.
        await VerifyFixAsync(
            """
            [Trellis.StringLength(10, Code = {|#0:"string.max-length"|})]
            public class Name { }
            """,
            """
            [Trellis.StringLength(10, Code = ValidationCodes.StringMaxLength)]
            public class Name { }
            """,
            Expect(0, "string.max-length", "ValidationCodes.StringMaxLength"));
}
