namespace Trellis.Core.Tests;

internal static class ValueTaskExtensions
{
    public static ValueTask<T> AsCompletedValueTask<T>(this T obj) => ValueTask.FromResult(obj);
    public static ValueTask AsCompletedValueTask(this Exception exception) => ValueTask.FromException(exception);
    public static ValueTask<T> AsCompletedValueTask<T>(this Exception exception) => ValueTask.FromException<T>(exception);
}