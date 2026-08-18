namespace Trellis.Core.Tests.Results.Extensions;

using System.Reflection;
using System.Runtime.CompilerServices;
using Trellis.Testing;

/// <summary>
/// Verifies the <see cref="OverloadResolutionPriorityAttribute"/> applied to the Task-delegate
/// overloads of <c>BindAsync</c> / <c>MapAsync</c> / <c>TapAsync</c> / <c>CheckAsync</c> /
/// <c>EnsureAsync</c> / <c>MatchAsync</c> on sync <see cref="Result{T}"/> receivers retires the
/// historical CS0121 ambiguity reported against the sibling <see cref="ValueTask{T}"/>-delegate
/// overloads when callers pass an inline async lambda whose body returns a synchronous
/// <see cref="Result{T}"/> (or non-Result value).
/// </summary>
/// <remarks>
/// Each call-site test below is a compile-time smoke test for the historical ambiguity:
/// pre-fix this file would not compile because the inline async lambda's natural return type
/// could target either the Task or ValueTask overload. Post-fix the Task overload wins by
/// priority and the call compiles. Runtime assertions also confirm the call dispatches to the
/// Task overload (returns <see cref="Task{T}"/>, not <see cref="ValueTask{T}"/>) and produces
/// the expected value, so a future revert of the attribute fails the build before tests run.
///
/// The <c>StronglyTypedValueTaskDelegate_StillResolvesToValueTaskOverload</c> tests prove
/// <see cref="OverloadResolutionPriorityAttribute"/> only ranks APPLICABLE candidates — passing
/// a strongly-typed <see cref="ValueTask{T}"/> delegate still resolves to the ValueTask
/// overload because the Task overload is not applicable.
/// </remarks>
public class OverloadResolutionPriorityTests : TestBase
{
    [Fact]
    public async Task BindAsync_InlineAsyncLambda_OnSyncResultReceiver_ResolvesToTaskOverload()
    {
        var result = Result.Ok(7);

        var bound = result.BindAsync(async i =>
        {
            await Task.Yield();
            return Result.Ok(i + 1);
        });

        bound.Should().BeAssignableTo<Task<Result<int>>>();
        (await bound).TryGetValue(out var v, out _).Should().BeTrue();
        v.Should().Be(8);
    }

    [Fact]
    public async Task MapAsync_InlineAsyncLambda_OnSyncResultReceiver_ResolvesToTaskOverload()
    {
        var result = Result.Ok(10);

        var mapped = result.MapAsync(async i =>
        {
            await Task.Yield();
            return i * 2;
        });

        mapped.Should().BeAssignableTo<Task<Result<int>>>();
        (await mapped).TryGetValue(out var v, out _).Should().BeTrue();
        v.Should().Be(20);
    }

    [Fact]
    public async Task TapAsync_InlineAsyncLambda_OnSyncResultReceiver_ResolvesToTaskOverload()
    {
        var result = Result.Ok(5);
        var observed = 0;

        var tapped = result.TapAsync(async v =>
        {
            await Task.Yield();
            observed = v;
        });

        tapped.Should().BeAssignableTo<Task<Result<int>>>();
        (await tapped).TryGetValue(out var roundTrip, out _).Should().BeTrue();
        roundTrip.Should().Be(5);
        observed.Should().Be(5);
    }

    [Fact]
    public async Task TapOnFailureAsync_InlineAsyncLambda_OnSyncResultReceiver_ResolvesToTaskOverload()
    {
        var result = Result.Fail<int>(new Error.NotFound(ResourceRef.For("Order", "abc-123")));
        var observed = false;

        var tapped = result.TapOnFailureAsync(async err =>
        {
            await Task.Yield();
            observed = err.Kind is not null;
        });

        tapped.Should().BeAssignableTo<Task<Result<int>>>();
        (await tapped).IsFailure.Should().BeTrue();
        observed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_InlineAsyncLambda_OnSyncResultReceiver_ResolvesToTaskOverload()
    {
        var result = Result.Ok(3);

        var checkResult = result.CheckAsync(async i =>
        {
            await Task.Yield();
            return Result.Ok(Unit.Default);
        });

        checkResult.Should().BeAssignableTo<Task<Result<int>>>();
        (await checkResult).TryGetValue(out var roundTrip, out _).Should().BeTrue();
        roundTrip.Should().Be(3);
    }

    [Fact]
    public async Task EnsureAsync_InlineAsyncLambda_OnSyncResultReceiver_ResolvesToTaskOverload()
    {
        var result = Result.Ok(42);
        var sentinelError = new Error.InvariantViolation("test_rule");

        var ensured = result.EnsureAsync(
            async i =>
            {
                await Task.Yield();
                return i > 0;
            },
            sentinelError);

        ensured.Should().BeAssignableTo<Task<Result<int>>>();
        (await ensured).TryGetValue(out var v, out _).Should().BeTrue();
        v.Should().Be(42);
    }

    [Fact]
    public async Task MatchAsync_InlineAsyncLambda_OnSyncResultReceiver_ResolvesToTaskOverload()
    {
        var result = Result.Ok(11);

        var matched = result.MatchAsync(
            async v =>
            {
                await Task.Yield();
                return $"ok:{v}";
            },
            async err =>
            {
                await Task.Yield();
                return $"err:{err.Kind}";
            });

        matched.Should().BeAssignableTo<Task<string>>();
        (await matched).Should().Be("ok:11");
    }

    [Fact]
    public async Task BindZipAsync_InlineAsyncLambda_OnSyncResultReceiver_ResolvesToTaskOverload()
    {
        var result = Result.Ok(7);

        var zipped = result.BindZipAsync(async i =>
        {
            await Task.Yield();
            return Result.Ok(i * 10);
        });

        zipped.Should().BeAssignableTo<Task<Result<(int, int)>>>();
        (await zipped).TryGetValue(out var v, out _).Should().BeTrue();
        v.Should().Be((7, 70));
    }

    [Fact]
    public async Task MapOnFailureAsync_InlineAsyncLambda_OnSyncResultReceiver_ResolvesToTaskOverload()
    {
        var result = Result.Fail<int>(new Error.NotFound(ResourceRef.For("Order", "abc-123")));

        var mapped = result.MapOnFailureAsync(async err =>
        {
            await Task.Yield();
            return (Error)new Error.Forbidden("test_policy");
        });

        mapped.Should().BeAssignableTo<Task<Result<int>>>();
        var actual = await mapped;
        actual.IsFailure.Should().BeTrue();
        actual.Error.Should().BeOfType<Error.Forbidden>();
    }

    // Note on SelectMany: Roslyn does not honor [OverloadResolutionPriority] across distinct
    // extension classes (the Task SelectMany is in ResultLinqExtensionsTaskRightAsync, the
    // ValueTask SelectMany in ResultLinqExtensionsValueTaskRightAsync). The priority attribute
    // is still applied to the Task overload as documentation and as a guard for any single-class
    // future refactor, but the inline-async-lambda CS0121 in LINQ query syntax is not
    // resolved by the attribute today. Consumers continue to use a typed local delegate or an
    // explicit method group when composing LINQ over Trellis Results across async return types.

    [Fact]
    public async Task BindAsync_StronglyTypedValueTaskDelegate_StillResolvesToValueTaskOverload()
    {
        var result = Result.Ok(2);

        Func<int, ValueTask<Result<int>>> next = async i =>
        {
            await Task.Yield();
            return Result.Ok(i + 1);
        };

        var bound = result.BindAsync(next);

        bound.Should().BeOfType<ValueTask<Result<int>>>();
        (await bound).TryGetValue(out var v, out _).Should().BeTrue();
        v.Should().Be(3);
    }

    [Fact]
    public async Task MapAsync_StronglyTypedValueTaskDelegate_StillResolvesToValueTaskOverload()
    {
        var result = Result.Ok(5);

        Func<int, ValueTask<int>> next = async i =>
        {
            await Task.Yield();
            return i * 2;
        };

        var mapped = result.MapAsync(next);

        mapped.Should().BeOfType<ValueTask<Result<int>>>();
        (await mapped).TryGetValue(out var v, out _).Should().BeTrue();
        v.Should().Be(10);
    }

    [Fact]
    public void OverloadResolutionPriority_AppliedConsistently_AcrossSyncReceiverTaskDelegateOverloads()
    {
        // Safety net: every public static `XxxAsync` method that takes a sync `Result<T>` receiver
        // and a `Func<..., Task<...>>` delegate (or, in the LINQ case, `SelectMany`) must carry
        // [OverloadResolutionPriority(1)] when an applicable sibling `Func<..., ValueTask<...>>`
        // overload exists on the same receiver. Asserting this via reflection means a future
        // contributor who adds a new sync-receiver+Task-delegate overload (or removes the
        // attribute from an existing one) gets a test failure with the specific method name,
        // not a downstream CS0121 from a random caller.
        var assembly = typeof(Result).Assembly;
        var missing = new List<string>();

        foreach (var type in assembly.GetExportedTypes().Where(t => t.IsAbstract && t.IsSealed))
        {
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                if (!IsCandidateSyncReceiverTaskOverload(method))
                    continue;

                var attr = method.GetCustomAttribute<OverloadResolutionPriorityAttribute>();
                if (attr is null || attr.Priority != 1)
                    missing.Add($"{type.Name}.{method.Name}({FormatParams(method)})");
            }
        }

        missing.Should().BeEmpty(
            "every sync-Result<T>-receiver method with a Func<..., Task<...>> delegate that has a sibling " +
            "Func<..., ValueTask<...>> overload must carry [OverloadResolutionPriority(1)] (priority 0 is " +
            "the default and would not resolve the Task/ValueTask ambiguity) so inline async lambdas with " +
            "ambiguous return types resolve to the Task overload");
    }

    private static bool IsCandidateSyncReceiverTaskOverload(MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 0)
            return false;

        var receiver = parameters[0].ParameterType;
        if (!IsClosedOrGenericResult(receiver))
            return false;

        var hasTaskDelegate = parameters.Skip(1).Any(p => DelegateReturns(p.ParameterType, typeof(Task<>), typeof(Task)));
        if (!hasTaskDelegate)
            return false;

        // Skip CancellationToken-bearing overloads — they don't compete with a no-token ValueTask sibling.
        if (parameters.Any(p => p.ParameterType == typeof(CancellationToken)))
            return false;

        // Confirm there's a sibling on the same type with the same receiver but ValueTask delegate(s).
        var sibling = method.DeclaringType!.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .FirstOrDefault(other =>
                other != method
                && other.Name == method.Name
                && other.IsGenericMethodDefinition == method.IsGenericMethodDefinition
                && SameSyncReceiverShape(other, receiver)
                && other.GetParameters().Length == parameters.Length
                && !other.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken))
                && other.GetParameters().Skip(1).Any(p => DelegateReturns(p.ParameterType, typeof(ValueTask<>), typeof(ValueTask))));

        return sibling is not null;
    }

    private static bool IsClosedOrGenericResult(Type receiver)
    {
        if (receiver.IsGenericType && receiver.GetGenericTypeDefinition() == typeof(Result<>))
            return true;
        return false;
    }

    private static bool SameSyncReceiverShape(MethodInfo candidate, Type receiver)
    {
        var p0 = candidate.GetParameters().FirstOrDefault();
        return p0 is not null && IsClosedOrGenericResult(p0.ParameterType);
    }

    private static bool DelegateReturns(Type type, Type asyncOpenGeneric, Type asyncNonGeneric)
    {
        // The parameter must be a delegate type. Generic non-delegates (e.g. `bool`, `int?`,
        // `Result<T>`) must not be incorrectly classified as delegates returning Task — the
        // earlier version of this guard had a logic bug that let non-delegate generics through
        // and then inferred a bogus "return type" from their last generic argument.
        if (!typeof(Delegate).IsAssignableFrom(type))
            return false;

        var invokeMethod = type.GetMethod("Invoke");
        if (invokeMethod is null)
            return false;

        var returnType = invokeMethod.ReturnType;

        if (returnType == asyncNonGeneric)
            return true;

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == asyncOpenGeneric)
            return true;

        return false;
    }

    private static string FormatParams(MethodInfo method) =>
        string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));
}