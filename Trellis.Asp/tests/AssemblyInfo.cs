// ga-09: TrellisAspOptions no longer carries ambient static state. Test parallelization
// is safe again, but several Trellis.Asp pipeline tests still mutate shared infrastructure
// (e.g. JSON converter caches), so cross-class parallelization stays disabled to keep CI
// deterministic. Revisit when those tests are isolated to their own service providers.
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
