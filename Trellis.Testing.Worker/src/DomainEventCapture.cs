namespace Trellis.Testing.Worker;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Stores domain events captured by the open-generic <see cref="DomainEventCaptureHandler{TEvent}"/>
/// and releases pending waiters when a matching event arrives. Registered as a singleton so
/// captures persist across the per-command DI scopes the mediator opens.
/// </summary>
/// <remarks>
/// <para>
/// Inserts are protected by an internal lock — multiple worker iterations may dispatch events
/// from parallel scopes, and parallel handler calls within a single dispatch wave can race with
/// test-side <see cref="WaitForAsync{TEvent}(System.Func{TEvent, bool}?, System.Threading.CancellationToken)"/>
/// callers.
/// </para>
/// <para>
/// Waiters use the snapshot-then-register-then-recheck pattern to avoid the classic
/// "event arrives between scan and TCS registration" race that produces flaky timeout failures.
/// </para>
/// </remarks>
internal sealed class DomainEventCapture
{
    private readonly object _gate = new();
    private readonly List<IDomainEvent> _events = [];
    private readonly List<IWaiter> _waiters = [];

    public IReadOnlyList<IDomainEvent> Snapshot()
    {
        lock (_gate)
            return [.. _events];
    }

    public IReadOnlyList<TEvent> SnapshotOf<TEvent>()
        where TEvent : IDomainEvent
    {
        lock (_gate)
        {
            // Materialize inside the lock so a concurrent Record can not append mid-enumeration.
            var matches = new List<TEvent>();
            foreach (var captured in _events)
                if (captured is TEvent typed)
                    matches.Add(typed);
            return matches;
        }
    }

    public void Record(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        List<MatchResult>? released = null;

        lock (_gate)
        {
            _events.Add(domainEvent);

            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                var waiter = _waiters[i];
                MatchOutcome outcome;
                Exception? predicateException = null;
                try
                {
                    outcome = waiter.TryMatch(domainEvent) ? MatchOutcome.Matched : MatchOutcome.NoMatch;
                }
                catch (Exception ex)
                {
                    // A predicate threw inside the publisher path. Without this guard the
                    // mediator would swallow/log the handler failure and the wait would hang
                    // until its real-time timeout, masking the real defect.
                    outcome = MatchOutcome.PredicateFailed;
                    predicateException = ex;
                }

                if (outcome == MatchOutcome.NoMatch) continue;

                released ??= [];
                released.Add(new MatchResult(waiter, outcome, predicateException));
                _waiters.RemoveAt(i);
            }
        }

        // Resolve outside the lock so handler continuations do not run under the gate.
        if (released is null) return;
        foreach (var result in released)
        {
            if (result.Outcome == MatchOutcome.PredicateFailed)
                result.Waiter.Fault(result.PredicateException!);
            else
                result.Waiter.Complete(domainEvent);
        }
    }

    public Task<TEvent> WaitForAsync<TEvent>(
        Func<TEvent, bool>? predicate,
        CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<TEvent> tcs;
        Waiter<TEvent> waiter;

        lock (_gate)
        {
            // Snapshot first so an event that arrived before WaitForAsync was called still satisfies the wait.
            foreach (var captured in _events)
            {
                if (captured is TEvent typed && (predicate is null || predicate(typed)))
                    return Task.FromResult(typed);
            }

            // RunContinuationsAsynchronously: TCS completions should not run on the dispatcher
            // thread that called Record(...). Without this flag a synchronous continuation would
            // execute under the capture lock's release, blocking other handler dispatches.
            tcs = new TaskCompletionSource<TEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            waiter = new Waiter<TEvent>(tcs, predicate);
            _waiters.Add(waiter);
        }

        return WaitWithCancellationAsync(tcs, waiter, cancellationToken);
    }

    private async Task<TEvent> WaitWithCancellationAsync<TEvent>(
        TaskCompletionSource<TEvent> tcs,
        Waiter<TEvent> waiter,
        CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        await using var registration = cancellationToken.Register(static state =>
        {
            var (capture, w) = ((DomainEventCapture, IWaiter))state!;
            // Only cancel when this callback wins the removal race; if Record(...) already
            // pulled the waiter out of the list, its TrySetResult is in flight and must win.
            if (capture.TryRemoveWaiter(w))
                w.Cancel();
        }, (this, (IWaiter)waiter));

        return await tcs.Task.ConfigureAwait(false);
    }

    private bool TryRemoveWaiter(IWaiter waiter)
    {
        lock (_gate)
            return _waiters.Remove(waiter);
    }

    private interface IWaiter
    {
        bool TryMatch(IDomainEvent domainEvent);
        void Complete(IDomainEvent domainEvent);
        void Cancel();
        void Fault(Exception exception);
    }

    private sealed class Waiter<TEvent>(
        TaskCompletionSource<TEvent> tcs,
        Func<TEvent, bool>? predicate)
        : IWaiter
        where TEvent : IDomainEvent
    {
        public bool TryMatch(IDomainEvent domainEvent)
        {
            if (domainEvent is not TEvent typed) return false;
            return predicate is null || predicate(typed);
        }

        public void Complete(IDomainEvent domainEvent) =>
            tcs.TrySetResult((TEvent)domainEvent);

        public void Cancel() =>
            tcs.TrySetCanceled();

        public void Fault(Exception exception) =>
            tcs.TrySetException(exception);
    }

    private enum MatchOutcome
    {
        NoMatch,
        Matched,
        PredicateFailed,
    }

    private readonly record struct MatchResult(IWaiter Waiter, MatchOutcome Outcome, Exception? PredicateException);
}