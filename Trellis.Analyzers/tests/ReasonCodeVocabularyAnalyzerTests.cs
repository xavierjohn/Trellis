namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.Testing;
using Xunit;

/// <summary>
/// Tests for <see cref="ReasonCodeVocabularyAnalyzer"/> (TRLS064).
/// </summary>
/// <remarks>
/// The rule's risk is not missing a case, it is firing on a legitimate application code. The docs
/// promise the freeze constrains Trellis rather than the application, so roughly half of these tests
/// assert silence.
/// </remarks>
public class ReasonCodeVocabularyAnalyzerTests
{
    private static async Task VerifyAsync(string body, params DiagnosticResult[] expected)
    {
        var source = $$"""
            public class Codes
            {
                public void Emit()
                {
            {{body}}
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ReasonCodeVocabularyAnalyzer>(source, expected);
        test.TestState.Sources.Add(("ReasonCodeStubs.cs", ReasonCodeTestStubs.Source));

        await test.RunAsync();
    }

    private static DiagnosticResult Expect(int location) =>
        AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ReasonCodeVocabulary).WithLocation(location);

    private static DiagnosticResult ExpectFrozen(int location, string code, string constant) =>
        Expect(location).WithArguments(code, FrozenExplanation(constant));

    private static string FrozenExplanation(string constant) =>
        $"is the framework reason code {constant}; emit it by constant, because a typo in a literal is "
        + "a silent wire break while a typo in a constant name does not compile";

    private static string SquatExplanation(string prefix) =>
        $"claims the framework namespace '{prefix}.*', whose meaning Trellis publishes, so a client "
        + "falling back on the prefix will read this application code as a framework one — application "
        + "codes are free-form, so pick a namespace the framework does not own";

    private const string ReservedExplanation =
        "claims the reserved 'error.*' namespace, which carries only the 'error.unspecified' sentinel; "
        + "a second member there makes the \"no reason available\" fallback lossy";

    private const string PlaceholderExplanation =
        "is the pre-vocabulary placeholder, which the boundary normalizes away; emit a real reason code "
        + "instead";

    // ---- Restating a frozen code (fixable) ----

    [Fact]
    public async Task Literal_matching_a_ValidationCodes_value_is_reported() =>
        await VerifyAsync("""
                    Failure.ForField("name", {|#0:"value.not-null"|});
            """, ExpectFrozen(0, "value.not-null", "ValidationCodes.ValueNotNull"));

    [Fact]
    public async Task Literal_matching_a_FaultCodes_value_is_reported() =>
        await VerifyAsync("""
                    Failure.ForReason({|#0:"state-machine.invalid-transition"|});
            """, ExpectFrozen(0, "state-machine.invalid-transition", "FaultCodes.StateMachineInvalidTransition"));

    [Fact]
    public async Task Single_segment_fault_code_is_reported() =>
        await VerifyAsync("""
                    Failure.ForReason({|#0:"not-implemented"|});
            """, ExpectFrozen(0, "not-implemented", "FaultCodes.NotImplemented"));

    [Fact]
    public async Task Positional_record_parameter_is_reported() =>
        await VerifyAsync("""
                    _ = new FieldViolation("name", {|#0:"format.guid"|});
                    _ = new RuleViolation({|#1:"value.not-null"|});
            """,
            ExpectFrozen(0, "format.guid", "ValidationCodes.FormatGuid"),
            ExpectFrozen(1, "value.not-null", "ValidationCodes.ValueNotNull"));

    [Fact]
    public async Task Named_argument_is_reported() =>
        await VerifyAsync("""
                    Failure.ForField(reasonCode: {|#0:"value.not-null"|}, propertyName: "name");
            """, ExpectFrozen(0, "value.not-null", "ValidationCodes.ValueNotNull"));

    [Fact]
    public async Task Reason_code_in_a_later_position_is_reported() =>
        // `For` puts the code third; keying on the parameter name rather than an index is what makes
        // the differing arities across the eleven factory overloads a non-issue.
        await VerifyAsync("""
                    Failure.For("Order", {|#0:"value.not-null"|}, 1);
            """, ExpectFrozen(0, "value.not-null", "ValidationCodes.ValueNotNull"));

    // ---- The reserved and squatted namespaces ----

    [Fact]
    public async Task Error_namespace_is_reported_as_reserved() =>
        await VerifyAsync("""
                    Failure.ForRule({|#0:"error.my-own"|});
            """, Expect(0).WithArguments("error.my-own", ReservedExplanation));

    [Fact]
    public async Task Framework_namespace_squatting_is_reported() =>
        await VerifyAsync("""
                    Failure.ForRule({|#0:"value.my-own-check"|});
            """, Expect(0).WithArguments("value.my-own-check", SquatExplanation("value")));

    [Fact]
    public async Task Hyphenated_framework_namespace_is_recognised() =>
        // `page-size` is a single segment containing a hyphen; splitting on the hyphen rather than the
        // dot would miss it.
        await VerifyAsync("""
                    Failure.ForRule({|#0:"page-size.made-up"|});
            """, Expect(0).WithArguments("page-size.made-up", SquatExplanation("page-size")));

    [Fact]
    public async Task Legacy_placeholder_is_reported_without_a_constant_suggestion() =>
        await VerifyAsync("""
                    Failure.ForRule({|#0:"validation.error"|});
            """, Expect(0).WithArguments("validation.error", PlaceholderExplanation));

    // ---- Silence: the application's own vocabulary ----

    [Fact]
    public async Task Application_code_in_its_own_namespace_is_clean() =>
        await VerifyAsync("""
                    Failure.ForRule("order.cancel-after-ship");
            """);

    [Fact]
    public async Task Bare_application_code_is_clean() =>
        // The trial agent that requested this rule wanted `required` flagged for duplicating
        // `value.not-null`. It is a synonym, not a collision, and the docs promise no analyzer
        // pressures the choice — so it stays silent, deliberately.
        await VerifyAsync("""
                    Failure.ForRule("required");
            """);

    [Fact]
    public async Task Legacy_namespace_is_not_reserved_against_applications() =>
        // `validation.error` itself is reported above, but the namespace is a pre-vocabulary artifact
        // rather than something the framework published a meaning for.
        await VerifyAsync("""
                    Failure.ForRule("validation.my-own");
            """);

    [Fact]
    public async Task Constant_reference_is_clean() =>
        // The whole rule collapses if this fires: a constant reference carries a constant *value*, so
        // testing the value alone would flag the exact shape the diagnostic tells authors to write.
        await VerifyAsync("""
                    Failure.ForField("name", ValidationCodes.ValueNotNull);
                    Failure.ForReason(FaultCodes.StateMachineInvalidTransition);
            """);

    [Fact]
    public async Task Local_constant_indirection_is_clean() =>
        // Accepted blind spot, asserted so it is a decision rather than a surprise.
        await VerifyAsync("""
                    const string code = "value.not-null";
                    Failure.ForRule(code);
            """);

    [Fact]
    public async Task Non_Trellis_method_with_a_reasonCode_parameter_is_clean() =>
        await VerifyAsync("""
                    NotTrellis.Unrelated.ForRule("value.not-null");
            """);

    [Fact]
    public async Task Frozen_value_in_a_non_reasonCode_parameter_is_clean() =>
        // The field name happens to read like a code; only the reason-code slot is the wire contract.
        await VerifyAsync("""
                    Failure.ForField("value.not-null", "order.rejected");
            """);

    [Fact]
    public async Task Empty_literal_is_clean() =>
        // Not this rule's business; an empty code is TRLS060's.
        await VerifyAsync("""
                    Failure.ForRule("");
            """);

    // ---- FluentValidation's WithErrorCode ----

    private static async Task VerifySourceAsync(string source, params DiagnosticResult[] expected)
    {
        var test = AnalyzerTestHelper.CreateDiagnosticTest<ReasonCodeVocabularyAnalyzer>(source, expected);
        test.TestState.Sources.Add(("ReasonCodeStubs.cs", ReasonCodeTestStubs.Source));

        await test.RunAsync();
    }

    private const string RuleBuilderHost = """
        using FluentValidation;

        public class Rules
        {
            public IRuleBuilderOptions<object, string> Builder = null!;

        """;

    [Fact]
    public async Task WithErrorCode_restating_a_frozen_code_is_reported() =>
        await VerifySourceAsync(RuleBuilderHost + """
                public void Apply() => Builder.WithErrorCode({|#0:"value.not-null"|});
            }
            """, ExpectFrozen(0, "value.not-null", "ValidationCodes.ValueNotNull"));

    [Fact]
    public async Task WithErrorCode_squatting_a_framework_namespace_is_reported() =>
        await VerifySourceAsync(RuleBuilderHost + """
                public void Apply() => Builder.WithErrorCode({|#0:"page-size.too-big"|});
            }
            """, Expect(0).WithArguments("page-size.too-big", SquatExplanation("page-size")));

    [Fact]
    public async Task WithErrorCode_carrying_an_application_code_is_clean() =>
        // The whole point of WithErrorCode is to name an application failure.
        await VerifySourceAsync(RuleBuilderHost + """
                public void Apply() => Builder.WithErrorCode("customer.unknown");
            }
            """);

    [Fact]
    public async Task WithErrorCode_referencing_the_constant_is_clean() =>
        await VerifySourceAsync(RuleBuilderHost + """
                public void Apply() => Builder.WithErrorCode(Trellis.ValidationCodes.ValueNotNull);
            }
            """);

    [Fact]
    public async Task WithErrorCode_outside_the_FluentValidation_namespace_is_clean() =>
        // Same method name, unrelated API — matching on name alone would be a false positive.
        await VerifySourceAsync("""
            public class Other
            {
                public void Apply() => NotTrellis.Unrelated.WithErrorCode("value.not-null");
            }
            """);

    [Fact]
    public async Task Test_helper_WithErrorCode_assertion_is_clean() =>
        // FluentValidation's TestHelper overload names its parameter `expectedErrorCode`, so the
        // parameter-name gate excludes it — which is the behavior we want, not an accident to tidy
        // away. Asserting the literal wire value is the deliberate pin that catches a renamed
        // constant; a test comparing the constant to itself stays green through exactly that break.
        await VerifySourceAsync("""
            using FluentValidation.TestHelper;

            public class Assertions
            {
                public ITestValidationContinuation Failures = null!;

                public void Assert() => Failures.WithErrorCode("value.not-null");
            }
            """);

    // ---- The Code property on Trellis primitive attributes ----

    [Fact]
    public async Task Attribute_Code_restating_a_frozen_code_is_reported() =>
        await VerifySourceAsync("""
            [Trellis.StringLength(10, Code = {|#0:"string.max-length"|})]
            public class Name { }
            """, ExpectFrozen(0, "string.max-length", "ValidationCodes.StringMaxLength"));

    [Fact]
    public async Task Attribute_Code_squatting_the_reserved_namespace_is_reported() =>
        await VerifySourceAsync("""
            [Trellis.StringLength(10, Code = {|#0:"error.too-long"|})]
            public class Name { }
            """, Expect(0).WithArguments("error.too-long", ReservedExplanation));

    [Fact]
    public async Task Attribute_Code_carrying_an_application_code_is_clean() =>
        // The documented reason to set Code at all; trellis-api-primitives.md's own example is this shape.
        await VerifySourceAsync("""
            [Trellis.StringLength(10, Code = "tenant.id.missing")]
            public class Name { }
            """);

    [Fact]
    public async Task Attribute_Code_referencing_the_constant_is_clean() =>
        await VerifySourceAsync("""
            [Trellis.StringLength(10, Code = Trellis.ValidationCodes.StringMaxLength)]
            public class Name { }
            """);

    [Fact]
    public async Task Attribute_property_other_than_Code_is_clean() =>
        // Only Code reaches the wire as a reason code.
        await VerifySourceAsync("""
            [Trellis.StringLength(10, Message = "value.not-null")]
            public class Name { }
            """);

    [Fact]
    public async Task Non_Trellis_attribute_with_a_Code_property_is_clean() =>
        await VerifySourceAsync("""
            [NotTrellis.Foreign(Code = "value.not-null")]
            public class Name { }
            """);

    [Fact]
    public async Task Duplicate_wire_value_reports_the_first_declaring_class()
    {        // The reflection guard in Trellis.Core already forbids a duplicate wire value, so this is
        // defensive — but "keep the first" was a claim in a comment with nothing holding it, and the
        // code did the opposite until a reviewer noticed. Pinning it makes the tie deterministic:
        // types are walked ValidationCodes then FaultCodes, so the ValidationCodes spelling wins.
        const string duplicateVocabulary = """
            namespace Trellis
            {
                public static class ValidationCodes
                {
                    public const string ValueNotNull = "value.not-null";
                }

                public static class FaultCodes
                {
                    public const string AlsoValueNotNull = "value.not-null";
                }

                public static class Failure
                {
                    public static object ForRule(string reasonCode) => null!;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ReasonCodeVocabularyAnalyzer>(
            """
            public class Codes
            {
                public void Emit() => Failure.ForRule({|#0:"value.not-null"|});
            }
            """,
            [ExpectFrozen(0, "value.not-null", "ValidationCodes.ValueNotNull")]);

        test.TestState.Sources.Add(("DuplicateVocabulary.cs", duplicateVocabulary));

        await test.RunAsync();
    }
}
