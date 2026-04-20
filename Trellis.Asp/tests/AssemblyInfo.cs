// Several test classes in Trellis.Asp.Tests assert behavior that depends on
// TrellisAspOptions static state (TrellisAspOptions._current, set via
// AddTrellisAsp / SetCurrent / ResetCurrent). Parallel execution across test
// classes races those mutations, producing intermittent CI failures where
// Error.Conflict maps to 400 instead of 409 (or vice versa). Disable
// cross-class parallelization in this assembly to serialize access.
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
