namespace Trellis.Analyzers.Tests;

/// <summary>
/// Self-contained Trellis surface for TRLS066 tests: <c>Result.Ensure</c> (returning <c>Result&lt;Unit&gt;</c>),
/// <c>ToResult</c> on nullable references and values, and <c>Combine</c>/<c>Map</c>/<c>Bind</c> over tuples.
/// </summary>
public static class UseToResultForNullableTestStubs
{
    public const string Source = """
        #nullable enable
        namespace Trellis
        {
            using System;

            public record struct Unit;

            public class Error
            {
                public Error(string code) { }
            }

            public readonly struct Result<T>
            {
                public bool IsSuccess { get; }
            }

            public static class Result
            {
                public static Result<Unit> Ensure(bool flag, Error error) => default;
                public static Result<Unit> Ensure(bool flag, Func<Error> errorFactory) => default;
                public static Result<(T1, T2)> Combine<T1, T2>(Result<T1> r1, Result<T2> r2) => default;
            }

            public static class NullableExtensions
            {
                public static Result<T> ToResult<T>(this T? nullable, Error error) where T : struct => default;
                public static Result<T> ToResult<T>(this T? obj, Error error) where T : class => default;
                public static Result<T> ToResult<T>(this T? nullable, Func<Error> errorFactory) where T : struct => default;
                public static Result<T> ToResult<T>(this T? obj, Func<Error> errorFactory) where T : class => default;
            }

            public static class CombineExtensions
            {
                public static Result<(T1, T2)> Combine<T1, T2>(this Result<T1> t1, Result<T2> t2) => default;
                public static Result<(T1, T2, T3)> Combine<T1, T2, T3>(this Result<(T1, T2)> t1, Result<T3> t3) => default;
            }

            public static class MapExtensions
            {
                public static Result<TResult> Map<T, TResult>(this Result<T> result, Func<T, TResult> map) => default;
                public static Result<(T1, T2)> Tap<T1, T2>(this Result<(T1, T2)> result, Action<T1, T2> action) => result;
                public static Result<TResult> Map<T1, T2, TResult>(this Result<(T1, T2)> result, Func<T1, T2, TResult> map) => default;
                public static Result<TResult> Map<T1, T2, T3, TResult>(this Result<(T1, T2, T3)> result, Func<T1, T2, T3, TResult> map) => default;
            }

            public static class BindExtensions
            {
                public static Result<TResult> Bind<T1, T2, TResult>(this Result<(T1, T2)> result, Func<T1, T2, Result<TResult>> bind) => default;
            }
        }
        """;
}
