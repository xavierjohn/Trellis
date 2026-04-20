namespace Benchmark;

using Trellis;

// Lightweight VOs used only by benchmarks. Previously sourced from SampleUserLibrary;
// inlined here after that sample was removed in the Showcase consolidation.
// EmailAddress is reused directly from Trellis.Primitives.EmailAddress.
public partial class LastName : RequiredString<LastName>
{
}
