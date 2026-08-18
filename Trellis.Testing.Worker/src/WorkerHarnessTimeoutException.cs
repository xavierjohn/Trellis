namespace Trellis.Testing.Worker;

using System;

/// <summary>
/// Raised by <see cref="WorkerHarness{TWorker}.WaitForEventAsync{TEvent}(System.TimeSpan?, System.Threading.CancellationToken)"/>
/// and <see cref="WorkerHarness{TWorker}.WaitForTickAsync(System.TimeSpan?, System.Threading.CancellationToken)"/>
/// when the awaited signal does not arrive within the configured timeout.
/// </summary>
/// <remarks>
/// The message includes the awaited event type or tick name, the timeout that elapsed, and
/// (where applicable) a summary of the events or tick signals captured so far so the test
/// failure points at the missing condition rather than at the wait-for call site.
/// </remarks>
public sealed class WorkerHarnessTimeoutException : TimeoutException
{
    /// <summary>Creates a new instance.</summary>
    public WorkerHarnessTimeoutException()
    {
    }

    /// <summary>Creates a new instance with the given message.</summary>
    /// <param name="message">A diagnostic message naming the awaited condition and the timeout that elapsed.</param>
    public WorkerHarnessTimeoutException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new instance with a message and inner exception.</summary>
    /// <param name="message">A diagnostic message naming the awaited condition and the timeout that elapsed.</param>
    /// <param name="innerException">The wait primitive's underlying exception, if any.</param>
    public WorkerHarnessTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}