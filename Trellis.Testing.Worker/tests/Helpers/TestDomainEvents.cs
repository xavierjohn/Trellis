namespace Trellis.Testing.Worker.Tests.Helpers;

/// <summary>
/// Domain events emitted by <see cref="TestWorker"/> on each successful tick. The worker
/// raises one event per tick so harness tests can assert dispatch order and time-driven
/// behaviour without coupling to a specific aggregate or repository.
/// </summary>
public sealed record TestTickCompletedEvent(int Iteration, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OtherTestEvent(string Note, DateTimeOffset OccurredAt) : IDomainEvent;