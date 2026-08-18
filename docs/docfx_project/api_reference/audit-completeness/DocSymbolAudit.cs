using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

/// <summary>
/// Reverse of the completeness audit: instead of asking "is every public API documented?",
/// this asks "does every API-shaped symbol named in the docs actually exist?".
///
/// The completeness audit cannot catch a confidently-documented type that was renamed or
/// never existed, because such a symbol has no counterpart in the assemblies to start from.
/// That is the failure mode this gate exists for.
/// </summary>
internal static class DocSymbolAudit
{
    // Backticked identifiers that look like API surface: PascalCase, optionally generic,
    // optionally dotted. Deliberately ignores lowercase words and short tokens.
    private static readonly Regex s_backtickedSymbol =
        new(@"`([A-Z][A-Za-z0-9_]*(?:<[^`]*>)?(?:\.[A-Za-z0-9_]+)*)`", RegexOptions.Compiled);

    // Diagnostic IDs (TRLS001, TRLSGEN102, TRLDOC004, CS0104...) are backticked all over the docs
    // but are identifier strings, not API surface, so no assembly will ever contain them.
    private static readonly Regex s_diagnosticId =
        new(@"^(?:TRLS|TRLSGEN|TRLDOC|CS|CA|IDE|SYSLIB)[0-9]{3,5}$", RegexOptions.Compiled);

    // Generic type parameters (TSelf, TAggregate, TResult) follow the T+PascalCase convention and
    // are never assembly symbols. Real types starting with T are safe: `TrellisActionResult` has a
    // lowercase second character, so it does not match.
    private static readonly Regex s_typeParameter = new(@"^T[A-Z][A-Za-z0-9]*$", RegexOptions.Compiled);

    // SQL keywords, HTTP verbs and status words (WHERE, DELETE, NULL, BLOB). C# API surface is
    // never all-caps, so these are documentation prose rather than symbols.
    private static readonly Regex s_allCapsWord = new(@"^[A-Z0-9_]+$", RegexOptions.Compiled);

    public static int Run(string docsDir, IEnumerable<string> assemblyPaths, MetadataLoadContext mlc)
    {
        var known = BuildKnownSymbolSet(assemblyPaths, mlc);
        var allowlist = LoadAllowlist(docsDir, out var allowlistPath);
        var unknownByFile = new SortedDictionary<string, List<(int Line, string Symbol)>>(StringComparer.Ordinal);
        var unusedAllowlistEntries = new HashSet<string>(allowlist, StringComparer.Ordinal);
        int unknownCount = 0;

        foreach (var file in Directory.EnumerateFiles(docsDir, "*.md").OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);

            // completeness-report.md is this tool's own generated output, living in the directory
            // it scans. Its contents are echoed from the assemblies, so auditing it is circular.
            if (string.Equals(name, "completeness-report.md", StringComparison.OrdinalIgnoreCase))
                continue;

            var lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in s_backtickedSymbol.Matches(lines[i]))
                {
                    // Validate every dotted segment, not just the head. Checking only the head
                    // would let `ExistingType.NoSuchMember` resolve on the strength of the type
                    // alone, which is precisely the drift this gate exists to catch: the member
                    // name is what a reader types.
                    foreach (var segment in Segments(match.Groups[1].Value))
                    {
                        if (segment.Length <= 3 || IsNotApiSurface(segment))
                            continue;

                        unusedAllowlistEntries.Remove(segment);

                        if (known.Contains(segment) || allowlist.Contains(segment))
                            continue;

                        if (!unknownByFile.TryGetValue(name, out var bucket))
                        {
                            bucket = [];
                            unknownByFile[name] = bucket;
                        }

                        if (!bucket.Any(entry => entry.Symbol == segment))
                        {
                            bucket.Add((i + 1, segment));
                            unknownCount++;
                        }
                    }
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== Documented-symbol audit (docs -> API) ===");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Known symbols loaded: {known.Count}; allowlisted doc-only names: {allowlist.Count}"));

        if (unknownCount == 0)
        {
            Console.WriteLine("All API-shaped symbols referenced in the docs resolve to a real symbol.");
            ReportUnusedAllowlistEntries(unusedAllowlistEntries, allowlistPath);
            return 0;
        }

        foreach (var (file, entries) in unknownByFile)
        {
            foreach (var (line, symbol) in entries.OrderBy(e => e.Line))
            {
                var fullPath = Path.Combine(docsDir, file);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{fullPath}({line},1): error TRLDOC005: '{symbol}' is referenced as API surface but does not exist in any loaded assembly. Fix the name, or if it is an illustrative type owned by the example rather than the framework, add it to {Path.GetFileName(allowlistPath)}."));
            }
        }

        Console.WriteLine();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{unknownCount} unresolved symbol(s)."));
        return 1;
    }

    private static void ReportUnusedAllowlistEntries(HashSet<string> unused, string allowlistPath)
    {
        if (unused.Count == 0)
            return;

        // Stale allowlist entries are how a gate quietly loses its teeth: an entry added for a
        // real example type keeps suppressing that name long after the docs stopped using it.
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Note: {unused.Count} allowlist entr(ies) in {Path.GetFileName(allowlistPath)} are no longer referenced by any doc and can be removed: {string.Join(", ", unused.OrderBy(x => x, StringComparer.Ordinal))}"));
    }

    private static bool IsNotApiSurface(string symbol) =>
        s_diagnosticId.IsMatch(symbol)
        || s_typeParameter.IsMatch(symbol)
        || s_allCapsWord.IsMatch(symbol)
        // Xxx is the repo's placeholder convention in shapes like UseXxx / AddXxxActorProvider.
        || symbol.Contains("Xxx", StringComparison.Ordinal);

    private static string FirstSegment(string symbol)
    {
        int cut = symbol.IndexOfAny(['<', '>', '.', '(']);
        return cut < 0 ? symbol : symbol[..cut];
    }

    /// <summary>
    /// Splits a captured symbol into the identifier segments worth resolving. Any segment may
    /// carry a generic argument list or call parentheses (<c>Maybe&lt;T&gt;.Map&lt;TOut&gt;</c>), so each
    /// is cut at the first <c>&lt;</c>, <c>&gt;</c>, <c>.</c> or <c>(</c>.
    /// </summary>
    private static IEnumerable<string> Segments(string symbol)
    {
        foreach (var part in symbol.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = FirstSegment(part);

            if (trimmed.Length > 0)
                yield return trimmed;
        }
    }

    private static HashSet<string> BuildKnownSymbolSet(IEnumerable<string> assemblyPaths, MetadataLoadContext mlc)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in assemblyPaths)
        {
            Assembly assembly;
            try { assembly = mlc.LoadFromAssemblyPath(path); }
            catch { continue; }

            Type[] types;
            // GetTypes rather than GetExportedTypes: the docs legitimately explain internal
            // machinery (EF conventions, interceptors, relays) that is not publicly exported,
            // and this gate asks "does this name exist?", not "is it public?".
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray(); }
            catch { continue; }

            foreach (var type in types)
            {
                AddTypeName(known, type);

                foreach (var segment in (type.Namespace ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries))
                    known.Add(segment);

                try
                {
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

                    foreach (var member in type.GetMembers(flags))
                    {
                        if (member.Name.StartsWith('<'))
                            continue;

                        known.Add(member.Name);

                        // Property/event accessors are exposed to docs by their bare name.
                        if (member.Name.StartsWith("get_", StringComparison.Ordinal) || member.Name.StartsWith("set_", StringComparison.Ordinal))
                            known.Add(member.Name[4..]);
                    }

                    foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                        AddTypeName(known, nested);
                }
                catch
                {
                    // A member listing failure must not mask the rest of the assembly.
                }
            }
        }

        return known;
    }

    private static void AddTypeName(HashSet<string> known, Type type)
    {
        var name = type.Name;
        int tick = name.IndexOf('`', StringComparison.Ordinal);
        known.Add(tick < 0 ? name : name[..tick]);
    }

    private static HashSet<string> LoadAllowlist(string docsDir, out string allowlistPath)
    {
        allowlistPath = Path.Combine(docsDir, "audit-completeness", "doc-only-symbols.txt");
        var allowlist = new HashSet<string>(StringComparer.Ordinal);

        if (!File.Exists(allowlistPath))
            return allowlist;

        foreach (var raw in File.ReadAllLines(allowlistPath))
        {
            var line = raw.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            allowlist.Add(line);
        }

        return allowlist;
    }
}
