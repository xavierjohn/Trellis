namespace Trellis.Mediator.Tests.Helpers;

using Microsoft.Extensions.Logging;

internal sealed class CapturingLogger<T> : ILogger<T>
{
    internal List<(LogLevel Level, EventId EventId, object? State)> Entries { get; } = [];

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, eventId, state));

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
