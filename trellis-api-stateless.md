# Trellis.Stateless — API Reference

> Part of the [Trellis API Reference](.). See also: trellis-api-results.md, trellis-api-ddd.md.

**Package:** `Trellis.Stateless` | **Namespace:** `Trellis.Stateless`

```csharp
Result<TState> FireResult<TState, TTrigger>(this StateMachine<TState, TTrigger> stateMachine, TTrigger trigger)
// Success → new state | Invalid transition → Error.Domain with code "state.machine.invalid.transition"
```

### LazyStateMachine\<TState, TTrigger\>

Defers state machine construction until first use, solving the ORM materialization problem where `stateAccessor` reads a default or uninitialized value before entity properties are populated.

```csharp
// Constructor — stateAccessor/stateMutator not invoked, configure not called
new LazyStateMachine<TState, TTrigger>(
    Func<TState> stateAccessor,
    Action<TState> stateMutator,
    Action<StateMachine<TState, TTrigger>> configure)

// Properties
StateMachine<TState, TTrigger> Machine { get; }  // Lazily creates and configures on first access

// Methods
Result<TState> FireResult(TTrigger trigger)  // Delegates to Machine.FireResult(trigger)
```

---
