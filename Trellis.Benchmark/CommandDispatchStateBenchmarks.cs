namespace Benchmark;

using BenchmarkDotNet.Attributes;
using Trellis;
using Trellis.Mediator;

/// <summary>
/// Isolates the real dispatch frame's complete lifecycle over matched fake handler/commit work.
/// The used-state baseline carries the same payload locally rather than through the accessor.
/// </summary>
[MemoryDiagnoser]
public class CommandDispatchStateBenchmarks
{
    private static readonly Result<int> s_response = Result.Ok(42);
    private readonly CommandDispatchState _state = new();
    private readonly UnitOfWork _unitOfWork = new();

    [Params(1, 2)]
    public int Depth { get; set; }

    [Params(false, true)]
    public bool RecordState { get; set; }

    [Benchmark(Baseline = true)]
    public ValueTask<Result<int>> WithoutFrames() => Dispatch(Depth, framed: false);

    [Benchmark]
    public ValueTask<Result<int>> WithFrames() => Dispatch(Depth, framed: true);

    private async ValueTask<Result<int>> Dispatch(int depth, bool framed)
    {
        using var scope = _unitOfWork.BeginScope();
        using var frame = framed ? CommandDispatchState.Begin() : null;
        var pending = RecordState
            ? framed ? _state.GetOrAdd(static () => new Pending()) : new Pending()
            : null;
        if (pending is not null)
            pending.Depth = depth;

        var response = depth > 1
            ? await Dispatch(depth - 1, framed).ConfigureAwait(false)
            : s_response;

        if (pending is not null)
        {
            if (framed && (!_state.TryGet<Pending>(out var restored)
                || !ReferenceEquals(restored, pending) || !_state.Remove<Pending>()))
                throw new InvalidOperationException("Dispatch state was not restored.");
            if (pending.Depth != depth)
                throw new InvalidOperationException("A child overwrote its parent's state.");
        }

        if (response.IsFailure)
            return response;
        var commit = await _unitOfWork.CommitAsync().ConfigureAwait(false);
        return commit.TryGetError(out var error) ? Result.Fail<int>(error) : response;
    }

    private sealed class Pending
    {
        public int Depth { get; set; }
    }

    private sealed class UnitOfWork : IUnitOfWork
    {
        private static readonly Task<Result<Unit>> s_commit = Result.Ok().AsTask();
        private int _depth;
        private Error? _rejection;

        public Task<Result<Unit>> CommitAsync(CancellationToken cancellationToken = default) =>
            _rejection is { } rejection ? Result.Fail(rejection).AsTask() : s_commit;

        public void MarkUncommittable(Error reason)
        {
            ArgumentNullException.ThrowIfNull(reason);
            _rejection ??= reason;
        }

        public IUnitOfWorkScope BeginScope() => new Scope(this, ++_depth == 1);

        private sealed class Scope(UnitOfWork owner, bool isOwner) : IUnitOfWorkScope
        {
            private bool _disposed;
            public bool IsOwner { get; } = isOwner;

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                owner._depth--;
            }
        }
    }
}
