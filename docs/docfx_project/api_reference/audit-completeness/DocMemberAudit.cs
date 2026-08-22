using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

/// <summary>
/// Third documentation gate, complementing the other two.
///
/// The completeness audit (TRLDOC008) asks "is every public API documented?". The
/// documented-symbol audit (TRLDOC005) asks "does every backticked name exist somewhere?".
/// Neither can catch a name that exists on the wrong type, because TRLDOC005 validates each
/// dotted segment independently: <c>Error.Validation.ForField</c> passes it because some <!-- v1-stale-ok: names a nonexistent API as the motivating defect -->
/// <c>Validation</c> and some <c>ForField</c> exist, even though <c>Error</c> has no
/// <c>Validation</c> member. Neither looks inside fenced code blocks at all, which is where
/// most API usage in these docs actually lives.
///
/// This gate resolves chains against the receiver: wherever a segment names a real type, the
/// following segment must be a real member (or nested type) of that type.
/// </summary>
internal static class DocMemberAudit
{
    // A dotted chain of identifiers. Generic argument lists and call parentheses are excluded
    // from the match and handled by the caller, so Maybe<T>.Map stops the chain at Maybe.
    private static readonly Regex s_chain =
        new(@"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+", RegexOptions.Compiled);

    private static readonly Regex s_csharpFence =
        new(@"^```\s*(?:csharp|c#|cs)\b(?<body>.*?)^```", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.Singleline);

    // Type declarations inside a fence. A document that defines its own example type owns that
    // name for the length of the document. `record` may be followed by `class` or `struct`, so
    // that pair is consumed before the identifier -- otherwise `record struct Cursor` registers
    // the local type as "struct" and Cursor is left exposed to the assembly index.
    private static readonly Regex s_localTypeDecl =
        new(@"\brecord\s+(?:class|struct)\s+([A-Za-z_][A-Za-z0-9_]*)|\b(?:class|record|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    // A trailing path/file extension marks the chain as a file name rather than a member access.
    private static readonly Regex s_fileExtension =
        new(@"^(?:cs|csproj|slnx|props|targets|json|md|yml|yaml|xml|txt|editorconfig|ps1|sln|razor|http)$", RegexOptions.Compiled);

    private static readonly Regex s_diagnosticId =
        new(@"^(?:TRLS|TRLSGEN|TRLDOC|CS|CA|IDE|SYSLIB)[0-9]{3,5}$", RegexOptions.Compiled);

    // Generic type parameters (T, TSelf, TAggregate) are never resolvable receivers.
    private static readonly Regex s_typeParameter = new(@"^T(?:[A-Z][A-Za-z0-9]*)?$", RegexOptions.Compiled);

    private static readonly Regex s_allCapsWord = new(@"^[A-Z0-9_]+$", RegexOptions.Compiled);

    private static bool IsNotApiSurface(string symbol) =>
        symbol.Length <= 2
        || s_diagnosticId.IsMatch(symbol)
        || s_typeParameter.IsMatch(symbol)
        || s_allCapsWord.IsMatch(symbol)
        // Xxx is the repo's placeholder convention in shapes like UseXxx / WhereXxx.
        || symbol.Contains("Xxx", StringComparison.Ordinal);

    public static int Run(string docsDir, IEnumerable<string> assemblyPaths, MetadataLoadContext mlc)
    {
        var index = BuildTypeIndex(assemblyPaths, mlc, out var namespaceSegments, out var universalExtensions);
        var allowlist = LoadAllowlist(docsDir, out var allowlistPath);

        Console.WriteLine();
        Console.WriteLine("=== Receiver-qualified member audit (docs -> API) ===");

        // Vacuity guard: if the type index is empty the gate would pass every document without
        // checking anything. TRLDOC013 takes the same stance on an empty diagnostic-id scan.
        if (index.Count == 0)
        {
            Console.WriteLine($"{docsDir}(1,1): error TRLDOC014: Loaded no types, so no member chain in any document can be verified. Fix the assembly discovery in this tool.");
            return 1;
        }

        var findings = new SortedDictionary<string, List<Finding>>(StringComparer.Ordinal);
        int checkedPairs = 0;
        int filesScanned = 0;
        int fencesScanned = 0;
        var unusedAllowlist = new HashSet<string>(allowlist, StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(docsDir, "*.md").OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);

            // Generated echo of the assemblies; auditing it is circular.
            if (string.Equals(name, "completeness-report.md", StringComparison.OrdinalIgnoreCase))
                continue;

            filesScanned++;
            var text = File.ReadAllText(file);
            var lineStarts = BuildLineStarts(text);
            var regions = EnumerateCodeRegions(text, ref fencesScanned);

            // Example types declared by this document shadow anything of the same simple name in
            // the assemblies. Without this, an illustrative `public enum DocumentState` is judged
            // against an unrelated runtime type that happens to share the name, and every one of
            // its members is reported as missing -- the gate would be loudest exactly where the
            // documentation is self-contained and correct.
            var localTypes = new HashSet<string>(StringComparer.Ordinal);

            foreach (var region in regions)
                foreach (Match decl in s_localTypeDecl.Matches(region.Text))
                    localTypes.Add(decl.Groups[1].Success ? decl.Groups[1].Value : decl.Groups[2].Value);

            foreach (var region in regions)
            {
                foreach (Match chain in s_chain.Matches(region.Text))
                {
                    var segments = chain.Value.Split('.');

                    // A trailing file extension means this is a path, not a member access
                    // (Directory.Build.props, CosmosIdempotencyContainer.cs).
                    if (s_fileExtension.IsMatch(segments[^1]))
                        continue;

                    // Skip any leading namespace qualification so a fully-qualified name is
                    // judged on its type, not on the namespace walk in front of it.
                    int head = 0;
                    while (head + 1 < segments.Length
                           && namespaceSegments.Contains(segments[head])
                           && !index.ContainsKey(segments[head]))
                        head++;

                    if (head + 1 >= segments.Length)
                        continue;

                    var receiver = segments[head];
                    var member = segments[head + 1];

                    // Only the head of a chain can be resolved without binding. An interior
                    // segment is almost always a value rather than a type -- in
                    // order.Id.Value, Id is a property that happens to share a type's simple
                    // name -- and judging it against that type is how this gate would produce
                    // false positives on correct documentation.
                    if (namespaceSegments.Contains(receiver) || IsNotApiSurface(receiver) || IsNotApiSurface(member))
                        continue;

                    if (localTypes.Contains(receiver))
                        continue;

                    if (!index.TryGetValue(receiver, out var members))
                        continue;

                    checkedPairs++;

                    var key = $"{receiver}.{member}";

                    if (members.Contains(member) || universalExtensions.Contains(member) || allowlist.Contains(key))
                    {
                        unusedAllowlist.Remove(key);
                        continue;
                    }

                    int line = LineOf(lineStarts, region.Offset + chain.Index);
                    if (!findings.TryGetValue(name, out var bucket))
                    {
                        bucket = [];
                        findings[name] = bucket;
                    }

                    if (!bucket.Any(f => f.Receiver == receiver && f.Member == member))
                        bucket.Add(new Finding(line, receiver, member));
                }
            }
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Types indexed: {index.Count}; files scanned: {filesScanned}; C# fences scanned: {fencesScanned}; receiver-qualified pairs checked: {checkedPairs}"));

        // Second vacuity guard. The index can be healthy while extraction silently matches
        // nothing -- a changed fence marker or a broken chain regex would do it -- and the gate
        // would then pass by examining zero call sites.
        if (checkedPairs == 0)
        {
            Console.WriteLine($"{docsDir}(1,1): error TRLDOC014: Extracted no receiver-qualified member accesses from any document, so this gate verified nothing. Fix the extraction patterns in this tool.");
            return 1;
        }

        int total = findings.Sum(f => f.Value.Count);

        if (total == 0)
        {
            Console.WriteLine("Every receiver-qualified member access in the docs resolves on its receiving type.");
            ReportUnusedAllowlist(unusedAllowlist, allowlistPath);
            return 0;
        }

        Console.WriteLine();
        foreach (var (file, entries) in findings)
        {
            foreach (var finding in entries.OrderBy(e => e.Line))
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{Path.Combine(docsDir, file)}({finding.Line},1): error TRLDOC014: '{finding.Receiver}.{finding.Member}' does not resolve: '{finding.Receiver}' is a real type but has no member or nested type named '{finding.Member}'. Fix the name, or if '{finding.Receiver}' is an example type that shadows a framework name, add '{finding.Receiver}.{finding.Member}' to {Path.GetFileName(allowlistPath)}."));
            }
        }

        Console.WriteLine();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{total} unresolved member access(es)."));
        return 1;
    }

    private readonly record struct Finding(int Line, string Receiver, string Member);

    private readonly record struct Region(string Text, int Offset);

    /// <summary>
    /// Blanks out string literals, char literals and comments, replacing them with spaces so
    /// that every surviving offset still maps to its original line. A dotted chain inside a
    /// string is not a member access -- <c>"Subscriptions.Read"</c> is a permission name and
    /// <c>$"Corrupt User.FirstName in row {id}"</c> is prose -- and a chain inside a comment is
    /// commentary rather than code. Interpolation holes are blanked along with their string:
    /// they do contain real code, but recovering it costs more than the coverage is worth.
    /// </summary>
    private static string Blank(string source)
    {
        var buffer = source.ToCharArray();
        int i = 0;

        while (i < buffer.Length)
        {
            char c = buffer[i];

            if (c == '/' && i + 1 < buffer.Length && buffer[i + 1] == '/')
            {
                while (i < buffer.Length && buffer[i] != '\n')
                    buffer[i++] = ' ';
            }
            else if (c == '/' && i + 1 < buffer.Length && buffer[i + 1] == '*')
            {
                while (i < buffer.Length && !(buffer[i] == '*' && i + 1 < buffer.Length && buffer[i + 1] == '/'))
                    BlankChar(buffer, ref i);

                for (int k = 0; k < 2 && i < buffer.Length; k++)
                    buffer[i++] = ' ';
            }
            else if (c == '"')
            {
                i = BlankString(buffer, i);
            }
            else if (c == '\'')
            {
                buffer[i++] = ' ';

                while (i < buffer.Length && buffer[i] != '\'' && buffer[i] != '\n')
                {
                    if (buffer[i] == '\\' && i + 1 < buffer.Length)
                        buffer[i++] = ' ';

                    BlankChar(buffer, ref i);
                }

                if (i < buffer.Length && buffer[i] == '\'')
                    buffer[i++] = ' ';
            }
            else
            {
                i++;
            }
        }

        return new string(buffer);
    }

    private static void BlankChar(char[] buffer, ref int i)
    {
        if (buffer[i] != '\n')
            buffer[i] = ' ';

        i++;
    }

    private static int BlankString(char[] buffer, int i)
    {
        // Raw string literal: a run of three or more quotes, closed by a run at least as long.
        int quotes = 0;
        while (i + quotes < buffer.Length && buffer[i + quotes] == '"')
            quotes++;

        if (quotes >= 3)
        {
            int fence = quotes;

            for (int k = 0; k < fence; k++)
                buffer[i++] = ' ';

            while (i < buffer.Length)
            {
                int run = 0;
                while (i + run < buffer.Length && buffer[i + run] == '"')
                    run++;

                if (run >= fence)
                {
                    for (int k = 0; k < run; k++)
                        buffer[i++] = ' ';

                    return i;
                }

                BlankChar(buffer, ref i);
            }

            return i;
        }

        // Verbatim strings escape a quote by doubling it; regular strings use a backslash. Both
        // orderings of the prefix are legal C#: $@"..." and @$"...".
        bool verbatim = (i > 0 && buffer[i - 1] == '@')
            || (i > 1 && buffer[i - 1] == '$' && buffer[i - 2] == '@');
        buffer[i++] = ' ';

        while (i < buffer.Length)
        {
            if (buffer[i] == '"')
            {
                if (verbatim && i + 1 < buffer.Length && buffer[i + 1] == '"')
                {
                    buffer[i++] = ' ';
                    buffer[i++] = ' ';
                    continue;
                }

                buffer[i++] = ' ';
                return i;
            }

            if (!verbatim && buffer[i] == '\\' && i + 1 < buffer.Length)
            {
                buffer[i++] = ' ';
                BlankChar(buffer, ref i);
                continue;
            }

            // An unterminated regular string means the fence is a fragment; stop at the newline
            // rather than blanking the rest of the snippet.
            if (!verbatim && buffer[i] == '\n')
                return i;

            BlankChar(buffer, ref i);
        }

        return i;
    }

    /// <summary>
    /// Yields the regions worth resolving: C# fence bodies only. Inline backticks are excluded
    /// deliberately -- prose shorthand like <c>DbSet.Include</c> or <c>Value.Length</c> names a
    /// member against the type a reader is thinking about rather than the type that declares it,
    /// and that is legitimate writing, not drift. Fenced code has no such licence: it is meant to
    /// compile. Backticked names remain covered by the documented-symbol audit (TRLDOC005).
    /// </summary>
    private static IEnumerable<Region> EnumerateCodeRegions(string text, ref int fenceCount)
    {
        var regions = new List<Region>();

        foreach (Match fence in s_csharpFence.Matches(text))
        {
            fenceCount++;
            var body = fence.Groups["body"];
            regions.Add(new Region(Blank(body.Value), body.Index));
        }

        return regions;
    }

    private static int[] BuildLineStarts(string text)
    {
        var starts = new List<int> { 0 };

        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n')
                starts.Add(i + 1);

        return [.. starts];
    }

    private static int LineOf(int[] lineStarts, int offset)
    {
        int lo = 0, hi = lineStarts.Length - 1;

        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (lineStarts[mid] <= offset) lo = mid; else hi = mid - 1;
        }

        return lo + 1;
    }

    private static Dictionary<string, HashSet<string>> BuildTypeIndex(
        IEnumerable<string> assemblyPaths,
        MetadataLoadContext mlc,
        out HashSet<string> namespaceSegments,
        out HashSet<string> universalExtensions)
    {
        var index = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        namespaceSegments = new HashSet<string>(StringComparer.Ordinal);
        universalExtensions = new HashSet<string>(StringComparer.Ordinal);

        // Extension methods are invoked through the receiver's simple name but declared on an
        // unrelated static class, so a declared-members-only index reports every one of them as
        // missing. Collected separately and merged once all assemblies are indexed.
        var extensions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var loaded = new List<Type>();

        // Simple names whose member set cannot be trusted, because at least one type carrying
        // that name failed to enumerate. Judged nowhere rather than judged partially.
        var poisoned = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in assemblyPaths)
        {
            Assembly assembly;
            try { assembly = mlc.LoadFromAssemblyPath(path); }
            catch { continue; }

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray(); }
            catch { continue; }

            foreach (var type in types)
            {
                loaded.Add(type);

                foreach (var segment in (type.Namespace ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries))
                    namespaceSegments.Add(segment);

                var simple = SimpleName(type);

                if (simple.StartsWith('<'))
                    continue;

                if (!index.TryGetValue(simple, out var members))
                {
                    members = new HashSet<string>(StringComparer.Ordinal);
                    index[simple] = members;
                }

                try
                {
                    // Inherited members included on purpose: docs legitimately write
                    // OrderId.Value, where Value comes from a Trellis primitive base class.
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

                    foreach (var member in type.GetMembers(flags))
                    {
                        if (member.Name.StartsWith('<'))
                            continue;

                        members.Add(member.Name);

                        if (member.Name.StartsWith("get_", StringComparison.Ordinal) || member.Name.StartsWith("set_", StringComparison.Ordinal))
                            members.Add(member.Name[4..]);
                    }

                    // FlattenHierarchy omits private inherited members and interface members.
                    for (var base_ = type.BaseType; base_ is not null; base_ = base_.BaseType)
                        foreach (var member in base_.GetMembers(flags))
                            if (!member.Name.StartsWith('<'))
                                members.Add(member.Name);

                    foreach (var iface in type.GetInterfaces())
                        foreach (var member in iface.GetMembers(flags))
                            if (!member.Name.StartsWith('<'))
                                members.Add(member.Name);

                    foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                        members.Add(SimpleName(nested));
                }
                catch
                {
                    // A member listing failure must not turn into a false positive. Removing the
                    // entry here would be order-dependent: the member set is shared by every type
                    // of the same simple name, so a removal discards members already gathered from
                    // a healthy namesake, and the next namesake recreates the entry with whatever
                    // happened to be enumerated after the failure. Poisoning the name instead
                    // drops that receiver for the whole run, whenever the failure occurs.
                    poisoned.Add(simple);
                }
            }
        }

        CollectExtensionMethods(loaded, extensions, universalExtensions);

        foreach (var (receiver, names) in extensions)
        {
            if (index.TryGetValue(receiver, out var members))
                members.UnionWith(names);
        }

        foreach (var name in poisoned)
            index.Remove(name);

        return index;
    }

    /// <summary>
    /// Maps each extension method onto the simple name of the type it extends. An extension on
    /// an open generic parameter (<c>this TSource source</c>) has no single receiver, so its name
    /// is recorded as universal and never reported against any receiver.
    /// </summary>
    private static void CollectExtensionMethods(
        List<Type> types,
        Dictionary<string, HashSet<string>> extensions,
        HashSet<string> universalExtensions)
    {
        foreach (var type in types)
        {
            // In a MetadataLoadContext even IsClass can throw, because classifying a type forces
            // its base type to resolve and not every referenced assembly is on the probe path.
            bool isStaticClass;
            try { isStaticClass = type.IsClass && type.IsAbstract && type.IsSealed; }
            catch { continue; }

            if (!isStaticClass)
                continue;

            MethodInfo[] methods;
            try { methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly); }
            catch { continue; }

            foreach (var method in methods)
            {
                bool isExtension;
                try { isExtension = method.GetCustomAttributesData().Any(a => a.AttributeType.Name == "ExtensionAttribute"); }
                catch { continue; }

                if (!isExtension)
                    continue;

                ParameterInfo[] parameters;
                try { parameters = method.GetParameters(); }
                catch { continue; }

                if (parameters.Length == 0)
                    continue;

                var receiverType = parameters[0].ParameterType;

                bool isOpenGeneric;
                try { isOpenGeneric = receiverType.IsGenericParameter; }
                catch { continue; }

                if (isOpenGeneric)
                {
                    universalExtensions.Add(method.Name);
                    continue;
                }

                var simple = SimpleName(receiverType);

                if (!extensions.TryGetValue(simple, out var names))
                {
                    names = new HashSet<string>(StringComparer.Ordinal);
                    extensions[simple] = names;
                }

                names.Add(method.Name);
            }
        }
    }

    private static string SimpleName(Type type)
    {
        var name = type.Name;
        int tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? name : name[..tick];
    }

    private static HashSet<string> LoadAllowlist(string docsDir, out string allowlistPath)
    {
        allowlistPath = Path.Combine(docsDir, "audit-completeness", "doc-only-members.txt");
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

    private static void ReportUnusedAllowlist(HashSet<string> unused, string allowlistPath)
    {
        if (unused.Count == 0)
            return;

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Note: {unused.Count} allowlist entr(ies) in {Path.GetFileName(allowlistPath)} are no longer referenced by any doc and can be removed: {string.Join(", ", unused.OrderBy(x => x, StringComparer.Ordinal))}"));
    }
}
