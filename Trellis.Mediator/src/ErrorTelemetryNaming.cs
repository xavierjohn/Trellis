namespace Trellis.Mediator;

/// <summary>
/// How the telemetry behaviors name an error case.
/// </summary>
/// <remarks>
/// Shared by <see cref="TracingBehavior{TMessage,TResponse}"/> and
/// <see cref="LoggingBehavior{TMessage,TResponse}"/> so a span tag and a log line name the same
/// failure identically. Two copies of a formatting rule are how a log stops being greppable by the
/// value an operator already took off a span.
/// </remarks>
internal static class ErrorTelemetryNaming
{
    /// <summary>
    /// Renders a nested error case as <c>Error.NotFound</c> rather than the bare <c>NotFound</c>,
    /// which on its own is ambiguous across the codebase.
    /// </summary>
    /// <param name="errorType">The runtime type of the error.</param>
    /// <returns>The name to publish.</returns>
    public static string FormatErrorTypeName(Type errorType)
    {
        var declaring = errorType.DeclaringType;
        return declaring is null ? errorType.Name : $"{declaring.Name}.{errorType.Name}";
    }
}
