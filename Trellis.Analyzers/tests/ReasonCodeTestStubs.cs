namespace Trellis.Analyzers.Tests;

/// <summary>
/// Stub source for the reason-code surfaces TRLS064 inspects.
/// </summary>
/// <remarks>
/// The vocabulary here is a deliberate subset of the real one. The analyzer reads the frozen codes out
/// of the compilation rather than from a table of its own, so a subset is enough to exercise every
/// branch — and using a subset proves the reading actually happens, which a copy of the full set would
/// not distinguish from a hard-coded list.
/// </remarks>
public static class ReasonCodeTestStubs
{
    public const string Source = """
        namespace Trellis
        {
            public static class ValidationCodes
            {
                public const string Unspecified = "error.unspecified";
                public const string LegacyUnspecified = "validation.error";
                public const string ValueNotNull = "value.not-null";
                public const string FormatGuid = "format.guid";
                public const string StringMaxLength = "string.max-length";
                public const string PageSizeOutOfRange = "page-size.out-of-range";
            }

            public static class FaultCodes
            {
                public const string NotImplemented = "not-implemented";
                public const string StateMachineInvalidTransition = "state-machine.invalid-transition";
            }

            public sealed record FieldViolation(string Field, string ReasonCode, string? Detail = null);

            public sealed record RuleViolation(string ReasonCode, string? Detail = null);

            public abstract record CodedFailure
            {
                protected CodedFailure() { }

                protected CodedFailure(string code) => Code = code;

                public string Code { get; init; } = "error.unspecified";
            }

            public sealed record CodedConflict(string Resource, string Code) : CodedFailure(Code);

            public sealed record CodedNotFound(string Resource) : CodedFailure;

            public static class Failure
            {
                public static object ForField(string propertyName, string reasonCode, string? detail = null) => null!;

                public static object ForRule(string reasonCode, string? detail = null) => null!;

                public static object ForReason(string reasonCode, string? detail = null) => null!;

                public static object For(string resourceType, string reasonCode, object? id = null) => null!;
            }

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class StringLengthAttribute : System.Attribute
            {
                public StringLengthAttribute(int maxLength) { }

                public string? Code { get; set; }

                public string? Message { get; set; }
            }
        }

        namespace FluentValidation
        {
            public interface IRuleBuilderOptions<T, TProperty> { }

            public static class DefaultValidatorOptions
            {
                public static IRuleBuilderOptions<T, TProperty> WithErrorCode<T, TProperty>(
                    this IRuleBuilderOptions<T, TProperty> rule, string errorCode) => rule;
            }
        }

        namespace FluentValidation.TestHelper
        {
            public interface ITestValidationContinuation { }

            public static class ValidationTestExtension
            {
                public static ITestValidationContinuation WithErrorCode(
                    this ITestValidationContinuation failures, string expectedErrorCode) => failures;
            }
        }

        namespace NotTrellis
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ForeignAttribute : System.Attribute
            {
                public string? Code { get; set; }
            }

            public static class Unrelated
            {
                public static object ForRule(string reasonCode, string? detail = null) => null!;

                public static object WithErrorCode(string errorCode) => null!;
            }

                public sealed record ForeignCoded
                {
                    public string? Code { get; init; }
                }
        }
        """;
}
