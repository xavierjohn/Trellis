namespace Trellis.Core.Tests.Errors;

using System.Reflection;
using System.Text.RegularExpressions;

/// <summary>
/// Invariant 5 — the shape rules the frozen vocabulary has to keep.
/// </summary>
/// <remarks>
/// These run over <see cref="ValidationCodes"/> by reflection rather than a hand-listed set, so a code
/// added later is covered without anyone remembering to add it here. That matters more than usual: the
/// vocabulary is frozen, so a malformed code merged once is on the wire permanently.
/// </remarks>
public class ValidationCodesTests
{
    private static readonly Regex Shape = new(@"^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)*$", RegexOptions.Compiled);

    private static IReadOnlyList<(string Name, string Value)> Codes { get; } =
        [.. new[] { typeof(ValidationCodes), typeof(FaultCodes) }
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!))
            .OrderBy(t => t.Item2, StringComparer.Ordinal)];

    /// <summary>
    /// The legacy placeholder is deliberately exempt: it is retained only so the ASP projection can
    /// recognize and replace it, and it is the one string the vocabulary is defined against rather
    /// than a member of it.
    /// </summary>
    private static IEnumerable<(string Name, string Value)> Owned =>
        Codes.Where(c => c.Value != ValidationCodes.LegacyUnspecified);

    [Fact]
    public void The_vocabulary_is_not_empty() =>
        // Guards the reflection above: a filter that silently matched nothing would make every
        // other test in this class vacuously true.
        Owned.Should().HaveCountGreaterThan(40);

    [Fact]
    public void Every_code_is_lowercase_dot_separated_and_hyphenated()
    {
        foreach (var (name, value) in Owned)
            Shape.IsMatch(value).Should().BeTrue(
                "{0} = '{1}' must be dot-separated namespaces of hyphen-separated lowercase words", name, value);
    }

    [Fact]
    public void No_code_is_declared_twice() =>
        Owned.GroupBy(c => c.Value, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} ({string.Join(", ", g.Select(c => c.Name))})")
            .Should().BeEmpty("one code means one thing, so two names for it would let producers drift apart");

    [Fact]
    public void No_code_is_another_code_prefix_at_a_dot_boundary()
    {
        // A client falls back from `string.exact-length` to `string`, so a code that is itself a
        // prefix of another would make that fallback ambiguous: the catalog could not tell whether
        // the entry it found was the code or the namespace it degrades to.
        var values = Owned.Select(c => c.Value).ToList();

        var offenders =
            from a in values
            from b in values
            where a != b && b.StartsWith(a + ".", StringComparison.Ordinal)
            select $"'{a}' is a prefix of '{b}'";

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void No_code_equals_a_namespace_in_use()
    {
        var namespaces = Owned
            .Select(c => c.Value)
            .Where(v => v.Contains('.'))
            .Select(v => v[..v.IndexOf('.')])
            .ToHashSet(StringComparer.Ordinal);

        Owned.Where(c => namespaces.Contains(c.Value))
            .Select(c => c.Value)
            .Should().BeEmpty("a bare namespace as a code would collide with the client's fallback key");
    }

    [Fact]
    public void The_sentinel_is_the_only_error_namespace_member() =>
        // Invariant 4 depends on `error.unspecified` being unambiguous. A second `error.*` code would
        // give the namespace a meaning beyond "no reason available" and make the fallback lossy.
        Owned.Where(c => c.Value.StartsWith("error.", StringComparison.Ordinal))
            .Select(c => c.Value)
            .Should().Equal([ValidationCodes.Unspecified]);

    [Fact]
    public void No_code_retains_snake_case() =>
        // Pins the section 5.11 renames. The regex above already rejects underscores, but this fails
        // with a message naming the convention rather than a regex, which is what a future author
        // reintroducing `page_size.out_of_range` needs to read.
        Owned.Where(c => c.Value.Contains('_'))
            .Select(c => c.Value)
            .Should().BeEmpty("the convention is hyphen-separated words, not snake_case");
}
