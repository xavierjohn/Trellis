namespace Trellis.Testing.AspNetCore.Http;

using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Produces <c>[MemberData]</c>-compatible test cases from a <c>.http</c> file.
/// Returns <c>IEnumerable&lt;object[]&gt;</c> rather than an xUnit-specific
/// <c>TheoryData&lt;T&gt;</c> so this helper carries no xUnit dependency from
/// the library itself; consumer test projects can freely wrap with
/// <c>TheoryData&lt;HttpFileRequest&gt;</c> if they prefer the typed variant.
/// </summary>
public static class HttpFileTheoryData
{
    /// <summary>
    /// Parses <paramref name="path"/> and yields one row per request, suitable
    /// for binding to a <c>[Theory]</c> parameter of type
    /// <see cref="HttpFileRequest"/>.
    /// </summary>
    /// <param name="path">Path to the <c>.http</c> file.</param>
    /// <param name="vars">Optional external variables.</param>
    /// <returns>An enumerable of single-element <c>object[]</c> rows.</returns>
    public static IEnumerable<object[]> FromFile(string path, IReadOnlyDictionary<string, string>? vars = null)
    {
        var content = File.ReadAllText(path);
        return HttpFileParser.Parse(content, vars).Select(r => new object[] { r }).ToArray();
    }
}