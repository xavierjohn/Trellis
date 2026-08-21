namespace Trellis.Analyzers.Tests;

using Xunit;

/// <summary>
/// Tests for <see cref="MustWithoutErrorCodeAnalyzer"/> (TRLS063).
/// </summary>
/// <remarks>
/// The analyzer's whole difficulty is deciding which part of a fluent chain a
/// <c>WithErrorCode</c> belongs to, so most of these tests vary chain shape rather than
/// varying the predicate.
/// </remarks>
public class MustWithoutErrorCodeAnalyzerTests
{
    private static async Task VerifyAsync(string validatorBody, params (int Location, string Method)[] expectedCalls)
    {
        var source = $$"""
            using FluentValidation;
            using System.Threading;
            using System.Threading.Tasks;

            public class Person
            {
                public string Name { get; set; } = "";
                public int Age { get; set; }
                public System.Collections.Generic.List<string> Tags { get; set; } = new();
            }

            public class PersonValidator : AbstractValidator<Person>
            {
                public PersonValidator()
                {
            {{validatorBody}}
                }
            }
            """;

        var expected = expectedCalls
            .Select(call => AnalyzerTestHelper
                .Diagnostic(DiagnosticDescriptors.MustWithoutErrorCode)
                .WithLocation(call.Location)
                .WithArguments(call.Method, call.Method == "MustAsync" ? "AsyncPredicateValidator" : "PredicateValidator"))
            .ToArray();

        var test = AnalyzerTestHelper.CreateDiagnosticTest<MustWithoutErrorCodeAnalyzer>(source, expected);
        test.TestState.Sources.Add(("FluentValidationStubs.cs", FluentValidationTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task Must_WithoutWithErrorCode_IsReported() =>
        await VerifyAsync("""
                    RuleFor(x => x.Name).{|#0:Must|}(n => n.Length > 2);
            """, (0, "Must"));

    [Fact]
    public async Task MustAsync_WithoutWithErrorCode_IsReported() =>
        await VerifyAsync("""
                    RuleFor(x => x.Name).{|#0:MustAsync|}((n, ct) => Task.FromResult(n.Length > 2));
            """, (0, "MustAsync"));

    [Fact]
    public async Task Must_FollowedByWithErrorCode_IsClean() =>
        await VerifyAsync("""
                    RuleFor(x => x.Name).Must(n => n.Length > 2).WithErrorCode("name.too.short");
            """);

    [Fact]
    public async Task Must_WithErrorCodeAfterOtherModifiers_IsClean() =>
        await VerifyAsync("""
                    RuleFor(x => x.Name)
                        .Must(n => n.Length > 2)
                        .WithMessage("too short")
                        .When(p => p.Age > 0)
                        .WithErrorCode("name.too.short");
            """);

    [Fact]
    public async Task Must_WithOnlyWithMessage_IsReported() =>
        await VerifyAsync("""
                    RuleFor(x => x.Name).{|#0:Must|}(n => n.Length > 2).WithMessage("too short");
            """, (0, "Must"));

    [Fact]
    public async Task NonMustValidators_AreNeverReported() =>
        await VerifyAsync("""
                    RuleFor(x => x.Name).NotEmpty();
                    RuleFor(x => x.Name).Matches("^a");
            """);

    [Fact]
    public async Task WithErrorCode_DoesNotCarryBackwardsToAnEarlierMust() =>
        await VerifyAsync("""
                    RuleFor(x => x.Name)
                        .{|#0:Must|}(n => n.Length > 2)
                        .Must(n => n.Length < 50)
                        .WithErrorCode("name.too.long");
            """, (0, "Must"));

    [Fact]
    public async Task WithErrorCode_DoesNotCarryAcrossAnInterveningValidator() =>
        await VerifyAsync("""
                    RuleFor(x => x.Name)
                        .{|#0:Must|}(n => n.Length > 2)
                        .Matches("^a")
                        .WithErrorCode("name.pattern");
            """, (0, "Must"));

    [Fact]
    public async Task EachUncodedMust_InOneChain_IsReportedSeparately() =>
        await VerifyAsync("""
                    RuleFor(x => x.Name)
                        .{|#0:Must|}(n => n.Length > 2)
                        .{|#1:Must|}(n => n.Length < 50);
            """, (0, "Must"), (1, "Must"));

    [Fact]
    public async Task Must_OnRuleForEach_IsReported() =>
        await VerifyAsync("""
                    RuleForEach(x => x.Tags).{|#0:Must|}(t => t.Length > 0);
            """, (0, "Must"));

    [Fact]
    public async Task Must_OnRuleForEach_WithErrorCode_IsClean() =>
        await VerifyAsync("""
                    RuleForEach(x => x.Tags).Must(t => t.Length > 0).WithErrorCode("tag.empty");
            """);

    [Fact]
    public async Task ParenthesizedChain_WithErrorCode_IsClean() =>
        // The code does apply to this Must; a syntax walk that stops at the parenthesis would
        // accuse an author who did exactly the right thing.
        await VerifyAsync("""
                    (RuleFor(x => x.Name).Must(n => n.Length > 2)).WithErrorCode("name.too.short");
            """);

    [Fact]
    public async Task ParenthesizedChain_WithCodeOnALaterComponent_IsStillReported() =>
        // Parentheses must not hide the rest of the chain either: the code here names Matches.
        await VerifyAsync("""
                    (RuleFor(x => x.Name).{|#0:Must|}(n => n.Length > 2)).Matches("^a").WithErrorCode("name.pattern");
            """, (0, "Must"));

    [Fact]
    public async Task MustInsideTheFluentValidationNamespaceOnANonRuleBuilder_IsNotReported()
    {
        // The namespace test passes here, so only the receiver check keeps this quiet.
        const string source = """
            using FluentValidation.Extras;

            public class Caller
            {
                public void Check(Gate gate)
                {
                    gate.Must(() => true);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<MustWithoutErrorCodeAnalyzer>(source);
        test.TestState.Sources.Add(("FluentValidationStubs.cs", FluentValidationTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task ChainAssignedToLocal_IsNotReported() =>
        // The rule escapes the statement, so a WithErrorCode may follow out of the analyzer's sight.
        await VerifyAsync("""
                    var rule = RuleFor(x => x.Name).Must(n => n.Length > 2);
                    rule.WithErrorCode("name.too.short");
            """);

    [Fact]
    public async Task ChainRefinedByConfigure_IsNotReported() =>
        // Configure hands out the raw rule, which can set ErrorCode directly.
        await VerifyAsync("""
                    RuleFor(x => x.Name)
                        .Must(n => n.Length > 2)
                        .Configure(r => r.ErrorCode = "name.too.short");
            """);

    [Fact]
    public async Task ChainPassedThroughAnUnknownExtension_IsNotReported()
    {
        // A user's own helper may wrap WithErrorCode. The analyzer cannot read it, so it stays quiet.
        const string source = """
            using FluentValidation;

            public class Person
            {
                public string Name { get; set; } = "";
            }

            public static class RuleExtensions
            {
                public static IRuleBuilderOptions<T, TProperty> AsDomainRule<T, TProperty>(
                    this IRuleBuilderOptions<T, TProperty> rule, string code) => rule.WithErrorCode(code);
            }

            public class PersonValidator : AbstractValidator<Person>
            {
                public PersonValidator()
                {
                    RuleFor(x => x.Name).Must(n => n.Length > 2).AsDomainRule("name.too.short");
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<MustWithoutErrorCodeAnalyzer>(source);
        test.TestState.Sources.Add(("FluentValidationStubs.cs", FluentValidationTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task MustInANamespaceMerelyPrefixedFluentValidation_IsNotReported()
    {
        // 'FluentValidationExtras' passes a StartsWith test but is not FluentValidation, and its
        // receiver is not an IRuleBuilder.
        const string source = """
            using FluentValidationExtras;

            public class Caller
            {
                public void Check(Guard guard)
                {
                    guard.Must(() => true);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<MustWithoutErrorCodeAnalyzer>(source);
        test.TestState.Sources.Add(("FluentValidationStubs.cs", FluentValidationTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task UserDeclaredMustOverloadInTheFluentValidationNamespace_IsNotReported()
    {
        // An application's own Must overload passes the namespace and receiver checks, but it may
        // name the failure itself — only FluentValidation's built-in Must is known to leave it unnamed.
        const string source = """
            using FluentValidation;

            public class Person
            {
                public string Name { get; set; } = "";
            }

            public class PersonValidator : AbstractValidator<Person>
            {
                public PersonValidator()
                {
                    RuleFor(x => x.Name).Must(n => n.Length > 2, "name.too.short");
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<MustWithoutErrorCodeAnalyzer>(source);
        test.TestState.Sources.Add(("FluentValidationStubs.cs", FluentValidationTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task ChainPassedThroughAUserHelperInTheFluentValidationNamespace_IsNotReported()
    {
        // Declaring rule extensions in namespace FluentValidation is a common convention so callers
        // need no extra using. Such a helper may wrap WithErrorCode, so it cannot be read as a
        // built-in validator starting a new component.
        const string source = """
            using FluentValidation;

            public class Person
            {
                public string Name { get; set; } = "";
            }

            public class PersonValidator : AbstractValidator<Person>
            {
                public PersonValidator()
                {
                    RuleFor(x => x.Name).Must(n => n.Length > 2).AsDomainRule("name.too.short");
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<MustWithoutErrorCodeAnalyzer>(source);
        test.TestState.Sources.Add(("FluentValidationStubs.cs", FluentValidationTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task MustNamedMethodOutsideFluentValidation_IsNotReported()
    {
        // The name alone must not be the trigger: an application's own Must is not a rule component.
        const string source = """
            public static class Guard
            {
                public static bool Must(bool condition) => condition;
            }

            public class Caller
            {
                public bool Check() => Guard.Must(true);
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<MustWithoutErrorCodeAnalyzer>(source);
        test.TestState.Sources.Add(("FluentValidationStubs.cs", FluentValidationTestStubs.Source));

        await test.RunAsync();
    }

    [Fact]
    public async Task CompilationWithoutFluentValidation_IsNotAnalyzed()
    {
        // Trellis.Analyzers ships to every consumer, most of whom never reference FluentValidation.
        const string source = """
            public class Unrelated
            {
                public bool Must(bool condition) => condition;

                public bool Check() => Must(true);
            }
            """;

        await AnalyzerTestHelper.CreateNoDiagnosticTest<MustWithoutErrorCodeAnalyzer>(source).RunAsync();
    }
}
