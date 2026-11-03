namespace Trellis.Testing;

/// <summary>An already-canceled token with its own identity, plus canceled tasks that carry it.</summary>
/// <remarks>
/// Each <see cref="Create"/> call owns a distinct <see cref="CancellationTokenSource"/>, so a test can prove an
/// operation preserved <em>this</em> token rather than substituting another canceled one.
/// </remarks>
public sealed class CanceledToken : IDisposable
{
    private readonly CancellationTokenSource _source = new();

    private CanceledToken()
    {
        _source.Cancel();
        Token = _source.Token;
    }

    /// <summary>Creates a new already-canceled token with a unique source.</summary>
    /// <returns>The canceled token holder; dispose it when the test ends.</returns>
    public static CanceledToken Create() => new();

    /// <summary>Gets the canceled token.</summary>
    public CancellationToken Token { get; }

    /// <summary>Gets a canceled <see cref="System.Threading.Tasks.Task"/> carrying <see cref="Token"/>.</summary>
    /// <returns>A canceled task.</returns>
    public Task ToTask() => Task.FromCanceled(Token);

    /// <summary>Gets a canceled <see cref="Task{TResult}"/> carrying <see cref="Token"/>.</summary>
    /// <typeparam name="T">The task result type.</typeparam>
    /// <returns>A canceled task.</returns>
    public Task<T> ToTask<T>() => Task.FromCanceled<T>(Token);

    /// <summary>Gets a canceled <see cref="System.Threading.Tasks.ValueTask"/> carrying <see cref="Token"/>.</summary>
    /// <returns>A canceled value task.</returns>
    public ValueTask ToValueTask() => ValueTask.FromCanceled(Token);

    /// <summary>Gets a canceled <see cref="ValueTask{TResult}"/> carrying <see cref="Token"/>.</summary>
    /// <typeparam name="T">The value task result type.</typeparam>
    /// <returns>A canceled value task.</returns>
    public ValueTask<T> ToValueTask<T>() => ValueTask.FromCanceled<T>(Token);

    /// <summary>Releases the underlying source. Safe to call more than once; <see cref="Token"/> stays valid and canceled.</summary>
    public void Dispose() => _source.Dispose();
}