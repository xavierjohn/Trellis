namespace Trellis.Core.Tests;

internal static class TaskExtensions
{
    public static Task<T> AsCompletedTask<T>(this T obj) => Task.FromResult(obj);
    public static Task AsCompletedTask(this Exception exception) => Task.FromException(exception);
    public static Task<T> AsCompletedTask<T>(this Exception exception) => Task.FromException<T>(exception);
}
