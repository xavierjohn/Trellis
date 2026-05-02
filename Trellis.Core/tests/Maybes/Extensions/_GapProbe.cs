namespace Trellis.Core.Tests.Maybes.Extensions;

// TDD probe: this file demonstrates that there is currently no SelectMany / Select / Where
// extension on Task<Maybe<T>> or ValueTask<Maybe<T>>, so query syntax over an async Maybe
// carrier fails to compile with CS1936 ("Could not find an implementation of the query
// pattern for source type 'Task<Maybe<T>>'. 'SelectMany' not found"). After the
// MaybeLinqExtensionsTaskAsync / MaybeLinqExtensionsValueTaskAsync families land, this
// probe will compile — at which point it gets repurposed as a positive smoke test or
// removed in favor of AsyncLinqTests.
public class _GapProbe
{
    [Fact]
    public async Task TaskMaybe_QuerySyntax_Compiles()
    {
        Task<Maybe<int>> a = Task.FromResult(Maybe.From(2));
        Task<Maybe<int>> b = Task.FromResult(Maybe.From(3));

        var combined = await (
            from x in a
            from y in b
            select x + y);

        combined.HasValue.Should().BeTrue();
        combined.Value.Should().Be(5);
    }
}