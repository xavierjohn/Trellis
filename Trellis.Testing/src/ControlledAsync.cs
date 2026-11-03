namespace Trellis.Testing;

/// <summary>A pending async source that a test explicitly completes, faults, or cancels.</summary>
/// <typeparam name="T">The value produced on completion.</typeparam>
/// <remarks>
/// Continuations run asynchronously. Always release pending sources in test cleanup.
/// The ValueTask receiver is Task-backed; it does not prove single-use IValueTaskSource behavior.
/// There is no implicit timeout, disposal, or cancellation of the operation under test.
/// </remarks>
public sealed class ControlledAsync<T>
{
    private readonly TaskCompletionSource<T> _source = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the shared Task receiver.</summary>
    public Task<T> Task => _source.Task;

    /// <summary>Gets a ValueTask receiver backed by the shared Task.</summary>
    public ValueTask<T> ValueTask => new(_source.Task);

    /// <summary>Completes the source if no terminal outcome has been set.</summary>
    /// <param name="value">The value to preserve unchanged.</param>
    /// <returns>True if this call completed the source; otherwise false.</returns>
    public bool TryComplete(T value) => _source.TrySetResult(value);

    /// <summary>Faults the source with the original exception if it is still pending.</summary>
    /// <param name="exception">The exception to propagate without wrapping.</param>
    /// <returns>True if this call faulted the source; otherwise false.</returns>
    public bool TryFail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return _source.TrySetException(exception);
    }

    /// <summary>Cancels the source, preserving the supplied token, if it is still pending.</summary>
    /// <param name="cancellationToken">The token to associate with the canceled source; it is not canceled by this method.</param>
    /// <returns>True if this call canceled the source; otherwise false.</returns>
    public bool TryCancel(CancellationToken cancellationToken = default) => _source.TrySetCanceled(cancellationToken);
}