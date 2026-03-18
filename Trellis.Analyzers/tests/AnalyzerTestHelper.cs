namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

/// <summary>
/// Helper for creating analyzer tests with Trellis references.
/// </summary>
public static class AnalyzerTestHelper
{
    /// <summary>
    /// Creates a test that verifies no diagnostics are produced.
    /// </summary>
    public static CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> CreateNoDiagnosticTest<TAnalyzer>(string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = WrapInNamespace(source),
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80
        };

        AddTrellisStubSource(test);
        return test;
    }

    /// <summary>
    /// Creates a test that verifies a specific diagnostic is produced.
    /// </summary>
    public static CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> CreateDiagnosticTest<TAnalyzer>(
        string source,
        params DiagnosticResult[] expectedDiagnostics)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = WrapInNamespace(source),
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80
        };

        AddTrellisStubSource(test);
        test.ExpectedDiagnostics.AddRange(expectedDiagnostics);
        return test;
    }

    private static void AddTrellisStubSource<TAnalyzer>(CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> test)
        where TAnalyzer : DiagnosticAnalyzer, new() =>
        // Add stub source code for Trellis types instead of referencing the actual assembly
        // This avoids framework version conflicts and makes tests self-contained
        test.TestState.Sources.Add(("TrellisStubs.cs", TrellisStubSource));

    private static string WrapInNamespace(string source) =>
        $$"""
            using Trellis;
            using System;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
                {{source}}
            }
            """;

    /// <summary>
    /// Creates a diagnostic result for the specified descriptor at a location.
    /// </summary>
    public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor) =>
        new DiagnosticResult(descriptor);

    /// <summary>
    /// Stub source code for Trellis types used in analyzer tests.
    /// </summary>
    private const string TrellisStubSource = """
        namespace Trellis
        {
            using System;
            using System.Threading.Tasks;

            [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
            public sealed class EnumValueAttribute : Attribute
            {
                public EnumValueAttribute(string value) { }
            }

            public abstract class RequiredEnum<TSelf>
                where TSelf : RequiredEnum<TSelf>
            {
                public string Value => string.Empty;

                public static Result<TSelf> TryFromName(string? name, string? fieldName = null) => default;
            }

            // IScalarValue interface stub
            public interface IScalarValue<TSelf, TPrimitive>
                where TSelf : IScalarValue<TSelf, TPrimitive>
                where TPrimitive : IComparable
            {
                static abstract Result<TSelf> TryCreate(TPrimitive value, string? fieldName = null);
                static virtual TSelf Create(TPrimitive value) => default!;
                TPrimitive Value { get; }
            }

            // Result<T> stub
            public readonly struct Result<T>
            {
                public bool IsSuccess { get; }
                public bool IsFailure => !IsSuccess;
                public T Value => IsSuccess ? default! : throw new InvalidOperationException();
                public Error Error => IsFailure ? default! : throw new InvalidOperationException();

                private Result(T value) { IsSuccess = true; }
                private Result(Error error) { IsSuccess = false; }
                
                public static implicit operator Result<T>(T value) => new Result<T>(value);
                public static implicit operator Result<T>(Error error) => new Result<T>(error);
                
                // MatchError stub
                public void MatchError(
                    Action<T> onSuccess,
                    Action<ValidationError> onValidation,
                    Action<NotFoundError> onNotFound,
                    Action<Error> onOther) { }

                public bool TryGetValue(out T value)
                {
                    value = default!;
                    return IsSuccess;
                }

                public T GetValueOrDefault(T defaultValue) => IsSuccess ? Value : defaultValue;

                public bool TryGetError(out Error error)
                {
                    error = default!;
                    return IsFailure;
                }
            }

            // Error stub (NOT abstract - can be instantiated directly)
            public class Error
            {
                public string Message { get; }
                public string Detail => Message;
                public Error(string message) { Message = message; }
                public static ValidationError Validation(string message) => new ValidationError(message);
                public static NotFoundError NotFound(string message) => new NotFoundError(message);
            }

            public sealed class ValidationError : Error
            {
                public ValidationError(string message) : base(message) { }
            }

            public sealed class NotFoundError : Error
            {
                public NotFoundError(string message) : base(message) { }
            }

            // Static Result factory class
            public static class Result
            {
                public static Result<T> Success<T>(T value) => value;
                public static Result<T> Failure<T>(Error error) => error;

                // Combine stubs
                public static Result<(T1, T2)> Combine<T1, T2>(Result<T1> r1, Result<T2> r2) => default;
                public static Result<(T1, T2, T3)> Combine<T1, T2, T3>(Result<T1> r1, Result<T2> r2, Result<T3> r3) => default;
            }

            // Maybe<T> stub
            public readonly struct Maybe<T>
            {
                public bool HasValue { get; }
                public bool HasNoValue => !HasValue;
                public T Value => HasValue ? default! : throw new InvalidOperationException();

                private Maybe(T value) { HasValue = true; }

                public static Maybe<T> None => default;
                public static Maybe<T> From(T value) => new Maybe<T>(value);
                
                public static implicit operator Maybe<T>(T value) => new Maybe<T>(value);
                
                // ToResult stub methods
                public Result<T> ToResult() => default;
                public Result<T> ToResult(Error error) => default;

                public bool TryGetValue(out T value)
                {
                    value = default!;
                    return HasValue;
                }
            }

            // Extension methods stub
            // Extension methods for Combine chaining (matching CombineTs.g.cs pattern)
            public static class CombineExtensions
            {
                // 2-element: Result<T1>.Combine(Result<T2>) → Result<(T1, T2)>
                public static Result<(T1, T2)> Combine<T1, T2>(this Result<T1> t1, Result<T2> t2) => default;

                // 3-element: Result<(T1, T2)>.Combine(Result<T3>) → Result<(T1, T2, T3)>
                public static Result<(T1, T2, T3)> Combine<T1, T2, T3>(this Result<(T1, T2)> t1, Result<T3> tc) => default;

                // 4-element
                public static Result<(T1, T2, T3, T4)> Combine<T1, T2, T3, T4>(this Result<(T1, T2, T3)> t1, Result<T4> tc) => default;

                // 5-element
                public static Result<(T1, T2, T3, T4, T5)> Combine<T1, T2, T3, T4, T5>(this Result<(T1, T2, T3, T4)> t1, Result<T5> tc) => default;

                // 6-element
                public static Result<(T1, T2, T3, T4, T5, T6)> Combine<T1, T2, T3, T4, T5, T6>(this Result<(T1, T2, T3, T4, T5)> t1, Result<T6> tc) => default;

                // 7-element
                public static Result<(T1, T2, T3, T4, T5, T6, T7)> Combine<T1, T2, T3, T4, T5, T6, T7>(this Result<(T1, T2, T3, T4, T5, T6)> t1, Result<T7> tc) => default;

                // 8-element
                public static Result<(T1, T2, T3, T4, T5, T6, T7, T8)> Combine<T1, T2, T3, T4, T5, T6, T7, T8>(this Result<(T1, T2, T3, T4, T5, T6, T7)> t1, Result<T8> tc) => default;

                // 9-element (maximum supported)
                public static Result<(T1, T2, T3, T4, T5, T6, T7, T8, T9)> Combine<T1, T2, T3, T4, T5, T6, T7, T8, T9>(this Result<(T1, T2, T3, T4, T5, T6, T7, T8)> t1, Result<T9> tc) => default;
            }

            public static class ResultExtensions
            {
                public static Result<TResult> Map<T, TResult>(this Result<T> result, Func<T, TResult> func) => default;
                public static Task<Result<TResult>> MapAsync<T, TResult>(this Result<T> result, Func<T, Task<TResult>> func) => Task.FromResult<Result<TResult>>(default);
                public static Result<TResult> Bind<T, TResult>(this Result<T> result, Func<T, Result<TResult>> func) => default;
                public static Task<Result<TResult>> BindAsync<T, TResult>(this Result<T> result, Func<T, Task<Result<TResult>>> func) => Task.FromResult<Result<TResult>>(default);
                public static Result<T> Tap<T>(this Result<T> result, Action<T> action) => result;
                public static Task<Result<T>> TapAsync<T>(this Result<T> result, Func<T, Task> func) => Task.FromResult(result);
                public static Result<T> TapOnFailure<T>(this Result<T> result, Action<Error> action) => result;
                public static Result<T> Ensure<T>(this Result<T> result, Func<T, bool> predicate, Error error) => result;
                public static Task<Result<T>> EnsureAsync<T>(this Result<T> result, Func<T, Task<bool>> predicate, Error error) => Task.FromResult(result);
                public static TResult Match<T, TResult>(this Result<T> result, Func<T, TResult> onSuccess, Func<Error, TResult> onFailure) => default!;
                public static Task<TResult> MatchAsync<T, TResult>(this Result<T> result, Func<T, Task<TResult>> onSuccess, Func<Error, Task<TResult>> onFailure) => Task.FromResult<TResult>(default!);
                public static void Switch<T>(this Result<T> result, Action<T> onSuccess, Action<Error> onFailure) { }
                public static Task SwitchAsync<T>(this Result<T> result, Func<T, Task> onSuccess, Func<Error, Task> onFailure) => Task.CompletedTask;
                public static TResult MatchError<T, TResult>(this Result<T> result, Func<T, TResult> onSuccess, Func<ValidationError, TResult> onValidation, Func<Error, TResult> onOther) => default!;
                public static void SwitchError<T>(this Result<T> result, Action<T> onSuccess, Action<ValidationError> onValidation, Action<Error> onOther) { }
            }

            public static class TaskResultExtensions
            {
                public static Task<Result<TResult>> Map<T, TResult>(this Task<Result<T>> resultTask, Func<T, TResult> func) => Task.FromResult<Result<TResult>>(default);
                public static Task<Result<TResult>> MapAsync<T, TResult>(this Task<Result<T>> resultTask, Func<T, Task<TResult>> func) => Task.FromResult<Result<TResult>>(default);
                public static Task<Result<TResult>> Bind<T, TResult>(this Task<Result<T>> resultTask, Func<T, Result<TResult>> func) => Task.FromResult<Result<TResult>>(default);
                public static Task<Result<TResult>> BindAsync<T, TResult>(this Task<Result<T>> resultTask, Func<T, Task<Result<TResult>>> func) => Task.FromResult<Result<TResult>>(default);
            }

            public static class MaybeExtensions
            {
                public static Maybe<TResult> Map<T, TResult>(this Maybe<T> maybe, Func<T, TResult> func) => default;
                public static Maybe<TResult> Bind<T, TResult>(this Maybe<T> maybe, Func<T, Maybe<TResult>> func) => default;
                public static TResult Match<T, TResult>(this Maybe<T> maybe, Func<T, TResult> onSome, Func<TResult> onNone) => default!;
                public static void Switch<T>(this Maybe<T> maybe, Action<T> onSome, Action onNone) { }
            }
        }
        """;
}