using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using System.Globalization;

string fwRoot = Environment.GetEnvironmentVariable("TRELLIS_FW_ROOT")
    ?? FindRepositoryRoot(Environment.CurrentDirectory)
    ?? @"C:\GitHub\Trellis\TrellisFramework";
string docsDir = Path.Combine(fwRoot, "docs", "docfx_project", "api_reference");

var packages = DiscoverPackages(fwRoot, docsDir).ToArray();

if (packages.Length == 0)
{
    Console.WriteLine($"No package projects with TrellisApiRefName found under {fwRoot}");
    return;
}

string? FindDll(PackageInfo package) {
    var projectDir = Path.GetDirectoryName(package.ProjectPath)!;
    var candidates = new[] {
        Path.Combine(projectDir, "bin", "Release", "net10.0", $"{package.AssemblyName}.dll"),
        Path.Combine(projectDir, "bin", "Debug",   "net10.0", $"{package.AssemblyName}.dll"),
        Path.Combine(projectDir, "bin", "Release", "netstandard2.0", $"{package.AssemblyName}.dll"),
        Path.Combine(projectDir, "bin", "Debug",   "netstandard2.0", $"{package.AssemblyName}.dll"),
    };
    return candidates.FirstOrDefault(File.Exists);
}

string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
var refPaths = new HashSet<string>(Directory.GetFiles(runtimeDir, "*.dll"), StringComparer.OrdinalIgnoreCase);

foreach (var package in packages) {
    var d = FindDll(package);
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

foreach (var package in packages) {
    var dll = FindDll(package);
    if (dll == null) { Console.WriteLine($"[{package.PackageName}] DLL not found, skipping"); continue; }
    var docPath = Path.Combine(docsDir, package.DocFile);
    if (!File.Exists(docPath)) { Console.WriteLine($"[{package.PackageName}] Doc missing: {docPath}"); continue; }
    var docText = File.ReadAllText(docPath).ToLowerInvariant();

    Assembly asm;
    try { asm = mlc.LoadFromAssemblyPath(dll); }
    catch (Exception ex) { Console.WriteLine($"[{package.PackageName}] Load failed: {ex.Message}"); continue; }

    Type[] types;
    try { types = asm.GetExportedTypes(); }
    catch (ReflectionTypeLoadException rtle) { types = rtle.Types.Where(t => t != null && t.IsPublic).Cast<Type>().ToArray(); }

    var undocTypes = new List<string>();
    var undocMembers = new List<string>();
    int memberTotal = 0;

    foreach (var t in types) {
        if (t.Name.StartsWith("<", StringComparison.Ordinal)) continue;
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
            if (m.Name.StartsWith("op_", StringComparison.Ordinal) || m.Name.StartsWith("get_", StringComparison.Ordinal) || m.Name.StartsWith("set_", StringComparison.Ordinal)
                || m.Name.StartsWith("add_", StringComparison.Ordinal) || m.Name.StartsWith("remove_", StringComparison.Ordinal)) continue;
            if (!docText.Contains(m.Name.ToLowerInvariant()))
                undocMembers.Add($"{t.FullName}::{m.Name}");
        }
    }

    summary.Add((package.PackageName, types.Length, undocTypes.Count, memberTotal, undocMembers.Count));
    var dedupedMembers = undocMembers.Distinct().OrderBy(x => x).ToList();
    sb.AppendLine();
    sb.AppendLine(CultureInfo.InvariantCulture, $"## {package.PackageName}");
    sb.AppendLine(CultureInfo.InvariantCulture, $"- Doc: `{package.DocFile}`");
    sb.AppendLine(CultureInfo.InvariantCulture, $"- Types: {types.Length} ({undocTypes.Count} undocumented)");
    sb.AppendLine(CultureInfo.InvariantCulture, $"- Members: {memberTotal} total, {undocMembers.Count} undocumented signatures, {dedupedMembers.Count} unique undocumented names");
    if (undocTypes.Any()) {
        sb.AppendLine();
        sb.AppendLine("### Undocumented types");
        foreach (var u in undocTypes.OrderBy(x => x)) sb.AppendLine(CultureInfo.InvariantCulture, $"- `{u}`");
    }
    if (dedupedMembers.Any()) {
        sb.AppendLine();
        sb.AppendLine("### Undocumented members on documented types (deduped — overloads collapsed)");
        foreach (var u in dedupedMembers) sb.AppendLine(CultureInfo.InvariantCulture, $"- `{u}`");
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

static string? FindRepositoryRoot(string startDirectory)
{
    var directory = new DirectoryInfo(startDirectory);

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Trellis.slnx")))
            return directory.FullName;

        directory = directory.Parent;
    }

    return null;
}

static IEnumerable<PackageInfo> DiscoverPackages(string fwRoot, string docsDir)
{
    return Directory
        .EnumerateFiles(fwRoot, "*.csproj", SearchOption.AllDirectories)
        .Where(path => path.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .Select(TryReadPackageInfo)
        .Where(info => info is not null)
        .Cast<PackageInfo>()
        .OrderBy(info => info.PackageName, StringComparer.Ordinal);

    PackageInfo? TryReadPackageInfo(string projectPath)
    {
        XDocument project;
        try { project = XDocument.Load(projectPath); }
        catch { return null; }

        var apiRefName = project
            .Descendants("TrellisApiRefName")
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);

        if (apiRefName is null)
            return null;

        var assemblyName = project
            .Descendants("AssemblyName")
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0)
            ?? Path.GetFileNameWithoutExtension(projectPath);

        var packageName = project
            .Descendants("PackageId")
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0)
            ?? assemblyName;

        var docFile = $"trellis-api-{apiRefName}.md";
        if (!File.Exists(Path.Combine(docsDir, docFile)))
            Console.WriteLine($"[{packageName}] Expected doc not found: {docFile}");

        return new PackageInfo(packageName, assemblyName, projectPath, docFile);
    }
}

internal sealed record PackageInfo(string PackageName, string AssemblyName, string ProjectPath, string DocFile);
