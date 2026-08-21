namespace Trellis.Analyzers.Tests;

/// <summary>
/// Shared stub source for the slice of FluentValidation's fluent surface that TRLS063 walks.
/// </summary>
/// <remarks>
/// Stubbed rather than referenced so the analyzer tests stay self-contained and pinned to the
/// chain shape the analyzer reasons about, instead of to whatever version the repository resolves.
/// </remarks>
public static class FluentValidationTestStubs
{
    /// <summary>
    /// Stub source providing <c>AbstractValidator</c>, <c>IRuleBuilderOptions</c>, the
    /// <c>Must</c>/<c>MustAsync</c> extensions, and the rule modifiers that may sit between a
    /// <c>Must</c> and its <c>WithErrorCode</c>.
    /// </summary>
    public const string Source = """
        namespace FluentValidation
        {
            using System;
            using System.Collections.Generic;
            using System.Linq.Expressions;
            using System.Threading;
            using System.Threading.Tasks;

            public interface IRuleBuilder<T, TProperty> { }

            public interface IRuleBuilderOptions<T, TProperty> : IRuleBuilder<T, TProperty> { }

            public interface IRuleBuilderInitial<T, TProperty> : IRuleBuilder<T, TProperty> { }

            public interface IRuleBuilderInitialCollection<T, TElement> : IRuleBuilder<T, TElement> { }

            public interface IValidationRule<T, TProperty>
            {
                string ErrorCode { get; set; }
            }

            public abstract class AbstractValidator<T>
            {
                public IRuleBuilderInitial<T, TProperty> RuleFor<TProperty>(Expression<Func<T, TProperty>> expression) => null!;

                public IRuleBuilderInitialCollection<T, TElement> RuleForEach<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression) => null!;
            }

            public static class DefaultValidatorExtensions
            {
                public static IRuleBuilderOptions<T, TProperty> Must<T, TProperty>(
                    this IRuleBuilder<T, TProperty> ruleBuilder, Func<TProperty, bool> predicate) => null!;

                public static IRuleBuilderOptions<T, TProperty> Must<T, TProperty>(
                    this IRuleBuilder<T, TProperty> ruleBuilder, Func<T, TProperty, bool> predicate) => null!;

                public static IRuleBuilderOptions<T, TProperty> MustAsync<T, TProperty>(
                    this IRuleBuilder<T, TProperty> ruleBuilder,
                    Func<TProperty, CancellationToken, Task<bool>> predicate) => null!;

                public static IRuleBuilderOptions<T, TProperty> NotEmpty<T, TProperty>(
                    this IRuleBuilder<T, TProperty> ruleBuilder) => null!;

                public static IRuleBuilderOptions<T, string> Matches<T>(
                    this IRuleBuilder<T, string> ruleBuilder, string pattern) => null!;
            }

            public static class DefaultValidatorOptions
            {
                public static IRuleBuilderOptions<T, TProperty> WithErrorCode<T, TProperty>(
                    this IRuleBuilderOptions<T, TProperty> rule, string errorCode) => rule;

                public static IRuleBuilderOptions<T, TProperty> WithMessage<T, TProperty>(
                    this IRuleBuilderOptions<T, TProperty> rule, string message) => rule;

                public static IRuleBuilderOptions<T, TProperty> WithName<T, TProperty>(
                    this IRuleBuilderOptions<T, TProperty> rule, string name) => rule;

                public static IRuleBuilderOptions<T, TProperty> When<T, TProperty>(
                    this IRuleBuilderOptions<T, TProperty> rule, Func<T, bool> predicate) => rule;

                public static IRuleBuilderOptions<T, TProperty> Configure<T, TProperty>(
                    this IRuleBuilderOptions<T, TProperty> rule, Action<IValidationRule<T, TProperty>> configurator) => rule;
            }

            // An application's own rule helper, declared in namespace FluentValidation so callers
            // need no extra using — a common convention, and one that may wrap WithErrorCode.
            public static class ApplicationRuleExtensions
            {
                public static IRuleBuilderOptions<T, TProperty> AsDomainRule<T, TProperty>(
                    this IRuleBuilderOptions<T, TProperty> rule, string code) => rule.WithErrorCode(code);

                public static IRuleBuilderOptions<T, TProperty> Must<T, TProperty>(
                    this IRuleBuilder<T, TProperty> rule, Func<TProperty, bool> predicate, string code) =>
                    DefaultValidatorExtensions.Must(rule, predicate).WithErrorCode(code);
            }
        }

        namespace FluentValidationExtras
        {
            using System;

            public sealed class Guard { }

            public static class GuardExtensions
            {
                public static bool Must(this Guard guard, Func<bool> predicate) => predicate();
            }
        }

        namespace FluentValidation.Extras
        {
            using System;

            public sealed class Gate { }

            public static class GateExtensions
            {
                // Genuinely inside the FluentValidation namespace — an application may declare this
                // so its helpers arrive with the same using — but the receiver is not a rule builder.
                public static bool Must(this Gate gate, Func<bool> predicate) => predicate();
            }
        }
        """;
}
