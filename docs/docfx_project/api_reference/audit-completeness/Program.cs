using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using System.Globalization;

string? fwRoot = Environment.GetEnvironmentVariable("TRELLIS_FW_ROOT")
    ?? FindRepositoryRoot(Environment.CurrentDirectory);

if (fwRoot is null)
{
    Console.Error.WriteLine("Could not locate the repository root (no Trellis.slnx found walking up from the current directory). Set TRELLIS_FW_ROOT to the framework root.");
    return 1;
}

string docsDir = Path.Combine(fwRoot, "docs", "docfx_project", "api_reference");

var packages = DiscoverPackages(fwRoot, docsDir).ToArray();

if (packages.Length == 0)
{
    Console.WriteLine($"No package projects with TrellisApiRefName found under {fwRoot}");
    return 1;
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
// The ASP.NET shared framework sits beside Microsoft.NETCore.App, so derive it from the runtime
// directory rather than hardcoding a Windows install path — this tool has to run on CI Linux too.
var sharedRoot = Path.GetDirectoryName(Path.GetDirectoryName(runtimeDir));
var aspnet = sharedRoot is null ? null : Path.Combine(sharedRoot, "Microsoft.AspNetCore.App");
if (aspnet is not null && Directory.Exists(aspnet)) {
    var ver = Directory.GetDirectories(aspnet)
        .OrderByDescending(d => Version.TryParse(Path.GetFileName(d).Split('-')[0], out var v) ? v : new Version(0, 0))
        .FirstOrDefault();
    if (ver != null) foreach (var f in Directory.GetFiles(ver, "*.dll")) refPaths.Add(f);
}

var resolver = new PathAssemblyResolver(refPaths);
using var mlc = new MetadataLoadContext(resolver);

// Symbols the docs legitimately name but that never reach a package bin folder: analyzer and
// source-generator assemblies (shipped as tooling, not lib) and third-party APIs such as EF Core
// (this repo does not copy NuGet assets into bin). Without both, a docs->API audit reports
// hundreds of false unknowns.
var auditRefPaths = new HashSet<string>(refPaths, StringComparer.OrdinalIgnoreCase);

// Only assemblies produced by a project that still exists may vouch for a name. A bare sweep of
// every Trellis*.dll under the repo also picks up output from deleted projects -- bin/ is
// gitignored, so removing a project leaves its assembly on disk indefinitely. Those stale
// assemblies are precisely full of removed APIs, so they silently satisfy the one check that
// exists to catch removed APIs, and only on developer machines: CI builds from a clean checkout
// and fails on names a local run resolved. Whitelisting by current project name keeps local and
// CI verdicts identical.
var currentAssemblyNames = new HashSet<string>(
    Directory
        .EnumerateFiles(fwRoot, "*.csproj", SearchOption.AllDirectories)
        .Select(Path.GetFileNameWithoutExtension)
        .Where(name => !string.IsNullOrEmpty(name))
        .Cast<string>(),
    StringComparer.OrdinalIgnoreCase);

foreach (var dll in Directory.EnumerateFiles(fwRoot, "Trellis*.dll", SearchOption.AllDirectories))
{
    if (!dll.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        continue;

    if (currentAssemblyNames.Contains(Path.GetFileNameWithoutExtension(dll)))
        auditRefPaths.Add(dll);
}

foreach (var dll in ResolveCentrallyManagedPackageAssemblies(fwRoot))
    auditRefPaths.Add(dll);

var auditResolver = new PathAssemblyResolver(auditRefPaths);
using var auditMlc = new MetadataLoadContext(auditResolver);

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

var symbolAuditExit = DocSymbolAudit.Run(docsDir, auditRefPaths, auditMlc);
var memberAuditExit = DocMemberAudit.Run(docsDir, auditRefPaths, auditMlc);

if (memberAuditExit != 0)
    symbolAuditExit = memberAuditExit;

var gapPackages = summary.Where(s => s.Item3 > 0 || s.Item5 > 0).ToList();
if (gapPackages.Count == 0)
{
    Console.WriteLine();
    Console.WriteLine("Completeness gate: every public type and member is named in its package's API reference.");
    return symbolAuditExit;
}

Console.WriteLine();
Console.WriteLine("=== TRLDOC008: undocumented public API ===");
foreach (var (p, _, ut, _, um) in gapPackages)
    Console.WriteLine($"error TRLDOC008: {p} has {ut} undocumented type(s) and {um} undocumented member signature(s).");
Console.WriteLine();
Console.WriteLine($"Every public type and member must be named in its package's API reference, because the reference is the only");
Console.WriteLine($"source an LLM consults before generating Trellis code -- an unnamed symbol is one it cannot use and may reinvent.");
Console.WriteLine($"See '{outPath}' for the per-symbol list, and docs/lint-api-reference.md (TRLDOC008) for how to resolve.");
return 1;

static IEnumerable<string> ResolveCentrallyManagedPackageAssemblies(string fwRoot)
{
    var propsPath = Path.Combine(fwRoot, "Directory.Packages.props");
    if (!File.Exists(propsPath))
        yield break;

    var nugetRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    if (!Directory.Exists(nugetRoot))
        yield break;

    // Most-preferred target framework first; the first one present in the package wins.
    string[] preferredTfms = ["net10.0", "net9.0", "net8.0", "netstandard2.1", "netstandard2.0"];

    foreach (var element in XDocument.Load(propsPath).Descendants().Where(e => e.Name.LocalName == "PackageVersion"))
    {
        var id = element.Attribute("Include")?.Value;
        var version = element.Attribute("Version")?.Value;

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
            continue;

        var libRoot = Path.Combine(nugetRoot, id.ToLowerInvariant(), version.ToLowerInvariant(), "lib");
        if (!Directory.Exists(libRoot))
            continue;

        var tfmDir = preferredTfms
            .Select(tfm => Path.Combine(libRoot, tfm))
            .FirstOrDefault(Directory.Exists);

        if (tfmDir is null)
            continue;

        foreach (var dll in Directory.EnumerateFiles(tfmDir, "*.dll"))
            yield return dll;
    }
}

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
