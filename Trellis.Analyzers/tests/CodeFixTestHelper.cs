namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

/// <summary>
/// Helper for creating code fix tests with Trellis references.
/// </summary>
public static class CodeFixTestHelper
{
    /// <summary>
    /// Creates a code fix test with the specified source and expected fixed code.
    /// </summary>
    public static CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier> CreateCodeFixTest<TAnalyzer, TCodeFix>(
        string source,
        string fixedSource,
        params DiagnosticResult[] expectedDiagnostics)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
        {
            TestCode = WrapInNamespace(source),
            FixedCode = WrapInNamespace(fixedSource),
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80
        };

        AddTrellisStubSource(test.TestState);
        AddTrellisStubSource(test.FixedState);
        test.ExpectedDiagnostics.AddRange(expectedDiagnostics);
        return test;
    }

    /// <summary>
    /// Creates a code fix test with multiple code actions (for testing specific code action index).
    /// </summary>
    public static CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier> CreateCodeFixTest<TAnalyzer, TCodeFix>(
        string source,
        string fixedSource,
        int codeActionIndex,
        params DiagnosticResult[] expectedDiagnostics)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        var test = CreateCodeFixTest<TAnalyzer, TCodeFix>(source, fixedSource, expectedDiagnostics);
        test.CodeActionIndex = codeActionIndex;
        return test;
    }

    /// <summary>
    /// Creates a diagnostic result for the specified descriptor.
    /// </summary>
    public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor) =>
        new DiagnosticResult(descriptor);

    private static void AddTrellisStubSource(SolutionState state) =>
        state.Sources.Add(("TrellisStubs.cs", TrellisStubSource));

    private static readonly string[] LineSeparators = ["\r\n", "\r", "\n"];

    private static string WrapInNamespace(string source)
    {
        // Indent all lines of the source by 4 spaces (to be inside the namespace)
        // Use proper line splitting that preserves empty lines
        var lines = source.Split(LineSeparators, StringSplitOptions.None);
        var indentedSource = string.Join(
            Environment.NewLine,
            lines.Select(line => string.IsNullOrWhiteSpace(line) ? line : "    " + line));

        return $$"""
            using Trellis;
            using System;
            using System.Threading.Tasks;

            namespace TestNamespace
            {
            {{indentedSource}}
            }
            """;
    }

    /// <summary>
    /// Stub source code for Trellis types used in code fix tests.
    /// </summary>
    private const string TrellisStubSource = """
        #pragma warning disable TRLS003 // Unsafe Result.Value access
        #pragma warning disable TRLS004 // Unsafe Result.Error access
        #pragma warning disable TRLS006 // Unsafe Maybe.Value access
        namespace Trellis
        {
            using System;
            using System.Threading.Tasks;

            // IScalarValue interface stub
            public interface IScalarValue<TSelf, TPrimitive>
                where TSelf : IScalarValue<TSelf, TPrimitive>
                where TPrimitive : IComparable
            {
                static abstract Result<TSelf> TryCreate(TPrimitive value, string? fieldName = null);
                TPrimitive Value { get; }
            }

            // Result<T> stub
            public readonly struct Result<T>
            {
                private readonly bool _isSuccess;
                private readonly T _value;
                private readonly Error _error;

                private Result(bool isSuccess, T value, Error error)
                {
                    _isSuccess = isSuccess;
                    _value = value;
                    _error = error;
                }

                public bool IsSuccess => _isSuccess;
                public bool IsFailure => !_isSuccess;
                public T Value => _isSuccess ? _value : throw new InvalidOperationException("Result is in failure state");
                public Error Error => !_isSuccess ? _error : throw new InvalidOperationException("Result is in success state");

                public static implicit operator Result<T>(T value) => new Result<T>(true, value, default);
                public static implicit operator Result<T>(Error error) => new Result<T>(false, default, error);

                public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure) =>
                    _isSuccess ? onSuccess(_value) : onFailure(_error);

                public T GetValueOrDefault() => _isSuccess ? _value : default;
                public T GetValueOrDefault(T defaultValue) => _isSuccess ? _value : defaultValue;
            }

            // Error stub
            public class Error
            {
                public string Code { get; }
                public string Detail { get; }

                public Error(string code, string detail)
                {
                    Code = code;
                    Detail = detail;
                }

                public static Error Validation(string detail) => new Error("Validation", detail);
                public static Error NotFound(string detail) => new Error("NotFound", detail);
            }

            // Static Result factory class
            public static class Result
            {
                public static Result<T> Ok<T>(T value) => value;
                public static Result<T> Fail<T>(Error error) => error;
            }

            // Result extensions stub (safe - no unsafe access)
            public static class ResultExtensions
            {
                public static Result<TResult> Map<T, TResult>(this Result<T> result, Func<T, TResult> func)
                {
                    if (!result.IsSuccess)
                        return Result.Fail<TResult>(default!);
                    return Result.Ok(func(result.Value));
                }

                public static Result<TResult> Bind<T, TResult>(this Result<T> result, Func<T, Result<TResult>> func)
                {
                    if (!result.IsSuccess)
                        return Result.Fail<TResult>(default!);
                    return func(result.Value);
                }

                public static Task<Result<TResult>> MapAsync<T, TResult>(this Result<T> result, Func<T, Task<TResult>> func)
                {
                    if (!result.IsSuccess)
                        return Task.FromResult(Result.Fail<TResult>(default!));
                    return func(result.Value).ContinueWith(t => Result.Ok(t.Result));
                }

                public static Task<Result<TResult>> BindAsync<T, TResult>(this Result<T> result, Func<T, Task<Result<TResult>>> func)
                {
                    if (!result.IsSuccess)
                        return Task.FromResult(Result.Fail<TResult>(default!));
                    return func(result.Value);
                }
            }

            // Maybe<T> stub
            public readonly struct Maybe<T>
            {
                private readonly bool _hasValue;
                private readonly T _value;

                private Maybe(bool hasValue, T value)
                {
                    _hasValue = hasValue;
                    _value = value;
                }

                public bool HasValue => _hasValue;
                public bool HasNoValue => !_hasValue;
                public T Value => _hasValue ? _value : throw new InvalidOperationException("Maybe has no value");

                public static Maybe<T> Some(T value) => new Maybe<T>(true, value);
                public static Maybe<T> None() => new Maybe<T>(false, default);
            }

            // EmailAddress stub (for TryCreate tests)
            public class EmailAddress : IScalarValue<EmailAddress, string>
            {
                private readonly string _value;
                private EmailAddress(string value) { _value = value; }
                public string Value => _value;

                public static Result<EmailAddress> TryCreate(string value, string? fieldName = null)
                {
                    if (string.IsNullOrEmpty(value) || !value.Contains('@'))
                        return Result.Fail<EmailAddress>(Error.Validation("Invalid email"));
                    return Result.Ok(new EmailAddress(value));
                }

                public static EmailAddress Create(string value)
                {
                    var result = TryCreate(value);
                    if (result.IsFailure)
                        throw new InvalidOperationException($"Failed to create EmailAddress: {result.Error.Detail}");
                    if (result.IsSuccess)
                        return result.Value;
                    throw new InvalidOperationException("Unexpected result state");
                }
            }

            // ValidationError stub (for type checking in tests)
            public class ValidationError : Error
            {
                public ValidationError(string detail) : base("Validation", detail) { }
            }

            // FluentAssertions-style stub for tests
            public static class FluentAssertionsStub
            {
                public static AssertionHelper<T> Should<T>(this T value) => new AssertionHelper<T>();
            }

            public class AssertionHelper<T>
            {
                public void Be(T expected) { }
                public void BeTrue() { }
                public void BeFalse() { }
                public void BeOfType<TExpected>() { }
                public void NotBeNull() { }
            }
        }
        #pragma warning restore TRLS003
        #pragma warning restore TRLS004
        #pragma warning restore TRLS006
        """;
}