using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

string fwRoot = Environment.GetEnvironmentVariable("TRELLIS_FW_ROOT") ?? @"C:\GitHub\Trellis\TrellisFramework";
string docsDir = Path.Combine(fwRoot, "docs", "api_reference");

var packages = new (string Pkg, string Doc)[] {
    ("Trellis.Core",                "trellis-api-core.md"),
    ("Trellis.Primitives",          "trellis-api-primitives.md"),
    ("Trellis.Mediator",            "trellis-api-mediator.md"),
    ("Trellis.FluentValidation",    "trellis-api-fluentvalidation.md"),
    ("Trellis.Asp",                 "trellis-api-asp.md"),
    ("Trellis.Authorization",       "trellis-api-authorization.md"),
    ("Trellis.EntityFrameworkCore", "trellis-api-efcore.md"),
    ("Trellis.Http",                "trellis-api-http.md"),
    ("Trellis.StateMachine",        "trellis-api-statemachine.md"),
    ("Trellis.Testing",             "trellis-api-testing-reference.md"),
    ("Trellis.Analyzers",           "trellis-api-analyzers.md"),
};

string? FindDll(string pkg) {
    var candidates = new[] {
        Path.Combine(fwRoot, pkg, "src", "bin", "Release", "net10.0", $"{pkg}.dll"),
        Path.Combine(fwRoot, pkg, "src", "bin", "Debug",   "net10.0", $"{pkg}.dll"),
        Path.Combine(fwRoot, pkg, "src", "bin", "Release", "netstandard2.0", $"{pkg}.dll"),
        Path.Combine(fwRoot, pkg, "src", "bin", "Debug",   "netstandard2.0", $"{pkg}.dll"),
    };
    return candidates.FirstOrDefault(File.Exists);
}

string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
var refPaths = new HashSet<string>(Directory.GetFiles(runtimeDir, "*.dll"), StringComparer.OrdinalIgnoreCase);

foreach (var (pkg, _) in packages) {
    var d = FindDll(pkg);
    if (d == null) continue;
    refPaths.Add(d);
    var dir = Path.GetDirectoryName(d)!;
    foreach (var f in Directory.GetFiles(dir, "*.dll")) refPaths.Add(f);
}
var aspnet = @"C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App";
if (Directory.Exists(aspnet)) {
    var ver = Directory.GetDirectories(aspnet).OrderByDescending(d => d).FirstOrDefault();
    if (ver != null) foreach (var f in Directory.GetFiles(ver, "*.dll")) refPaths.Add(f);
}

var resolver = new PathAssemblyResolver(refPaths);
using var mlc = new MetadataLoadContext(resolver);

string[] skipMembers = { "Equals","GetHashCode","ToString","GetType","MemberwiseClone","Finalize","Deconstruct","<Clone>$",
    // Roslyn analyzer/codefix base-class overrides — known contract, doc once at the package level.
    "SupportedDiagnostics","RegisterCodeFixesAsync","GetFixAllProvider","FixableDiagnosticIds","Initialize" };

var sb = new StringBuilder();
var summary = new List<(string Pkg, int Types, int UndocTypes, int Members, int UndocMembers)>();

foreach (var (pkg, doc) in packages) {
    var dll = FindDll(pkg);
    if (dll == null) { Console.WriteLine($"[{pkg}] DLL not found, skipping"); continue; }
    var docPath = Path.Combine(docsDir, doc);
    if (!File.Exists(docPath)) { Console.WriteLine($"[{pkg}] Doc missing: {docPath}"); continue; }
    var docText = File.ReadAllText(docPath).ToLowerInvariant();

    Assembly asm;
    try { asm = mlc.LoadFromAssemblyPath(dll); }
    catch (Exception ex) { Console.WriteLine($"[{pkg}] Load failed: {ex.Message}"); continue; }

    Type[] types;
    try { types = asm.GetExportedTypes(); }
    catch (ReflectionTypeLoadException rtle) { types = rtle.Types.Where(t => t != null && t.IsPublic).Cast<Type>().ToArray(); }

    var undocTypes = new List<string>();
    var undocMembers = new List<string>();
    int memberTotal = 0;

    foreach (var t in types) {
        if (t.Name.StartsWith("<")) continue;
        var simple = t.Name.Contains('`') ? t.Name.Substring(0, t.Name.IndexOf('`')) : t.Name;
        if (!docText.Contains(simple.ToLowerInvariant())) {
            undocTypes.Add(t.FullName ?? simple);
            continue;
        }
        var bf = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        IEnumerable<MemberInfo> members =
            t.GetMethods(bf).Where(m => !m.IsSpecialName).Cast<MemberInfo>()
            .Concat(t.GetProperties(bf))
            .Concat(t.GetEvents(bf))
            .Concat(t.GetFields(bf).Where(f => !f.IsSpecialName));
        foreach (var m in members) {
            memberTotal++;
            if (skipMembers.Contains(m.Name)) continue;
            if (m.Name.StartsWith("op_") || m.Name.StartsWith("get_") || m.Name.StartsWith("set_")
                || m.Name.StartsWith("add_") || m.Name.StartsWith("remove_")) continue;
            if (!docText.Contains(m.Name.ToLowerInvariant()))
                undocMembers.Add($"{t.FullName}::{m.Name}");
        }
    }

    summary.Add((pkg, types.Length, undocTypes.Count, memberTotal, undocMembers.Count));
    var dedupedMembers = undocMembers.Distinct().OrderBy(x => x).ToList();
    sb.AppendLine();
    sb.AppendLine($"## {pkg}");
    sb.AppendLine($"- Types: {types.Length} ({undocTypes.Count} undocumented)");
    sb.AppendLine($"- Members: {memberTotal} total, {undocMembers.Count} undocumented signatures, {dedupedMembers.Count} unique undocumented names");
    if (undocTypes.Any()) {
        sb.AppendLine();
        sb.AppendLine("### Undocumented types");
        foreach (var u in undocTypes.OrderBy(x => x)) sb.AppendLine($"- `{u}`");
    }
    if (dedupedMembers.Any()) {
        sb.AppendLine();
        sb.AppendLine("### Undocumented members on documented types (deduped — overloads collapsed)");
        foreach (var u in dedupedMembers) sb.AppendLine($"- `{u}`");
    }
}

Console.WriteLine();
Console.WriteLine("=== Summary ===");
Console.WriteLine($"{"Package",-32} {"Types",6} {"UndocT",6} {"Members",8} {"UndocM",6}");
foreach (var (p,t,ut,m,um) in summary)
    Console.WriteLine($"{p,-32} {t,6} {ut,6} {m,8} {um,6}");

var outPath = Path.Combine(docsDir, "completeness-report.md");
File.WriteAllText(outPath, "# API Reference Completeness Report\n" + sb.ToString());
Console.WriteLine();
Console.WriteLine($"Report written: {outPath}");
