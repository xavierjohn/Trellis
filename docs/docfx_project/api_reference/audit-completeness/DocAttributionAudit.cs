using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

/// <summary>
/// Fourth documentation gate, covering the blind spot shared by the other three.
///
/// The completeness audit (TRLDOC008) asks "is every public API documented?". The
/// documented-symbol audit (TRLDOC005) asks "does every backticked name exist somewhere?".
/// The member audit (TRLDOC014) resolves dotted chains against their receiver. None of them
/// can catch a <b>bare</b> member name whose surrounding prose attributes it to a type that
/// does not declare it, because the name really does exist — on some other type.
///
/// That is not hypothetical. When <c>Error.ReasonCode</c> was collapsed into <c>Error.Code</c>,
/// ten references survived the sweep telling readers to use <c>ReasonCode</c> on an
/// <c>Error</c>, three of them in public XML docs. Every gate passed, because
/// <c>FieldViolation.ReasonCode</c> and <c>RuleViolation.ReasonCode</c> still exist and a bare
/// name carries no receiver to check it against.
///
/// So this gate inverts the question. Rather than trying to infer which type a sentence means —
/// which needs natural language, not reflection — it takes a curated ledger of names that are
/// declared by some types and known to be mis-attributed to others, and requires every mention
/// of one to name its receiver. <c>FieldViolation.ReasonCode</c> passes; a bare
/// <c>ReasonCode</c> does not. A name a reader must guess the owner of is a name they will
/// guess wrong.
/// </summary>
internal static class DocAttributionAudit
{
    // Inline code spans only. A fenced block is real code, where a bare name can be a legitimate
    // named argument (`new FieldViolation(ReasonCode: "x")`); prose is where attribution is
    // asserted rather than compiled, and prose is where every observed defect lived.
    private static readonly Regex s_inlineCode = new(@"`([^`\r\n]+)`", RegexOptions.Compiled);

    public static int Run(string docsDir, IEnumerable<string> assemblyPaths, MetadataLoadContext mlc)
    {
        var ledger = LoadLedger(docsDir, out var ledgerPath, out var ledgerExists);

        Console.WriteLine();
        Console.WriteLine("=== Member-attribution audit (bare names must name their receiver) ===");

        // A missing ledger and an empty one are the same value but not the same event, and only
        // one of them is benign. If the file is gone -- renamed, moved, lost to a path change --
        // the gate would report the same green as a full clean run, which is the miswiring the
        // vacuity guard below exists to refuse.
        if (!ledgerExists)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{ledgerPath}(1,1): error TRLDOC015: the ambiguous-member ledger is missing, so this gate would enforce nothing. Restore the file, or delete this audit deliberately if it is no longer wanted."));
            return 1;
        }

        // An empty ledger, by contrast, is a legitimate resting state: entries self-expire, so
        // removing the last one leaves genuinely nothing to enforce. Failing here would force
        // dead entries to be kept alive purely to keep the gate quiet.
        if (ledger.Count == 0)
        {
            Console.WriteLine($"No entries in {Path.GetFileName(ledgerPath)}; nothing to enforce.");
            return 0;
        }

        var declaringTypes = BuildDeclaringTypeMap(assemblyPaths, mlc, ledger);
        int failures = 0;

        // A ledger entry earns its place by being ambiguous: the name must still be declared
        // somewhere. Once nothing declares it, TRLDOC005 already rejects every mention on the
        // stronger ground that the name does not exist, and the entry here is dead weight that
        // reads as coverage without adding any.
        foreach (var name in ledger.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (declaringTypes.TryGetValue(name, out var owners) && owners.Count > 0)
                continue;

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{ledgerPath}(1,1): error TRLDOC015: '{name}' is listed as an ambiguous member but no loaded type declares it. Remove the entry — TRLDOC005 already rejects mentions of a name that does not exist."));
            failures++;
        }

        var filesScanned = 0;
        var spansExamined = 0;

        foreach (var (dir, recursive) in ScannedDirectories(docsDir))
        {
            if (!Directory.Exists(dir))
                continue;

            var search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (var file in Directory.EnumerateFiles(dir, "*.md", search).OrderBy(p => p, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(file);

                // This tool's own generated output echoes the assemblies, so auditing it is circular.
                if (string.Equals(name, "completeness-report.md", StringComparison.OrdinalIgnoreCase))
                    continue;

                var lines = File.ReadAllLines(file);
                filesScanned++;

                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match span in s_inlineCode.Matches(lines[i]))
                    {
                        // A span carrying a parameter list is a signature or a call -- code quoted
                        // verbatim, not an attribution claim. `sealed record (string ReasonCode, ...)`
                        // is how a member's own declaration is documented, and demanding a receiver
                        // there would demand a signature the compiler would reject. Every drift this
                        // gate exists to catch is receiverless prose, which has no parameter list.
                        if (span.Groups[1].Value.Contains('('))
                            continue;

                        spansExamined++;

                        foreach (var unqualified in UnqualifiedMentions(span.Groups[1].Value, ledger))
                        {
                            var owners = declaringTypes.TryGetValue(unqualified, out var set)
                                ? string.Join(", ", set.OrderBy(x => x, StringComparer.Ordinal).Select(t => $"{t}.{unqualified}"))
                                : "(none)";

                            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                                $"{file}({i + 1},1): error TRLDOC015: '{unqualified}' is named without a receiver, and is declared only by: {owners}. Write the owning type so a reader can tell which one is meant, or if this mention is about a different type, the reference is stale."));
                            failures++;
                        }
                    }
                }
            }
        }

        // Refuse to pass by checking nothing, on the same reasoning as the sibling gates: a gate
        // whose inputs vanished reports the same green as a gate that verified the whole doc set,
        // and that green is most convincing exactly when it is least deserved.
        if (filesScanned == 0 || spansExamined == 0)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{ledgerPath}(1,1): error TRLDOC015: the attribution audit examined {filesScanned} file(s) and {spansExamined} inline code span(s), so it verified nothing. The docs directory or the inline-span pattern is wrong."));
            failures++;
        }

        if (failures == 0)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"All {ledger.Count} ambiguous member name(s) are attributed to a receiver wherever they appear. Files scanned: {filesScanned}; inline spans examined: {spansExamined}."));
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{failures} unattributed or stale member reference(s)."));
        return 1;
    }

    /// <summary>
    /// Yields each ledger name that appears in <paramref name="code"/> as a whole word without a
    /// receiver. A mention preceded by <c>.</c> is attributed — <c>FieldViolation.ReasonCode</c>
    /// and <c>violation.ReasonCode</c> both name something a reader can follow — so only a
    /// genuinely bare mention is reported.
    /// </summary>
    private static IEnumerable<string> UnqualifiedMentions(string code, IReadOnlyCollection<string> ledger)
    {
        foreach (var name in ledger)
        {
            int from = 0;

            while (true)
            {
                int at = code.IndexOf(name, from, StringComparison.Ordinal);
                if (at < 0)
                    break;

                from = at + name.Length;

                if (at > 0 && (IsIdentifierChar(code[at - 1]) || code[at - 1] == '.'))
                    continue;

                if (from < code.Length && IsIdentifierChar(code[from]))
                    continue;

                yield return name;
                break;
            }
        }
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static IEnumerable<(string Directory, bool Recursive)> ScannedDirectories(string docsDir)
    {
        yield return (docsDir, false);

        // Articles are where the motivating defect actually lived, and they are not covered by
        // the other gates. The walk is recursive because the per-diagnostic pages under
        // articles/analyzers/ are exactly the prose most likely to name a member bare. ADRs sit
        // in a sibling directory and are therefore never reached: they are dated decision
        // records, and an ADR describing the API as it stood is correct precisely by being stale.
        var projectRoot = Path.GetDirectoryName(docsDir.TrimEnd(Path.DirectorySeparatorChar));

        if (!string.IsNullOrEmpty(projectRoot))
            yield return (Path.Combine(projectRoot, "articles"), true);
    }

    private static Dictionary<string, HashSet<string>> BuildDeclaringTypeMap(
        IEnumerable<string> assemblyPaths,
        MetadataLoadContext mlc,
        IReadOnlyCollection<string> ledger)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var wanted = new HashSet<string>(ledger, StringComparer.Ordinal);

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
                try
                {
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

                    foreach (var member in type.GetMembers(flags))
                    {
                        if (!wanted.Contains(member.Name))
                            continue;

                        if (!map.TryGetValue(member.Name, out var owners))
                        {
                            owners = new HashSet<string>(StringComparer.Ordinal);
                            map[member.Name] = owners;
                        }

                        owners.Add(TypeName(type));
                    }
                }
                catch
                {
                    // A member listing failure must not mask the rest of the assembly.
                }
            }
        }

        return map;
    }

    private static string TypeName(Type type)
    {
        var name = type.Name;
        int tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? name : name[..tick];
    }

    private static HashSet<string> LoadLedger(string docsDir, out string ledgerPath, out bool ledgerExists)
    {
        ledgerPath = Path.Combine(docsDir, "audit-completeness", "ambiguous-members.txt");
        ledgerExists = File.Exists(ledgerPath);

        if (!ledgerExists)
            return new HashSet<string>(StringComparer.Ordinal);

        return File.ReadAllLines(ledgerPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);
    }
}
