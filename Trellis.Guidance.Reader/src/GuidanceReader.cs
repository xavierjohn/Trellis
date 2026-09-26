namespace Trellis.Guidance.Reader;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>The result of inspecting one resolved NuGet package in one project/target scope.</summary>
public enum GuidanceStatus
{
    /// <summary>All declared guidance was verified.</summary>
    Valid,
    /// <summary>The package exists and does not declare guidance.</summary>
    NoManifest,
    /// <summary>The manifest declares a schema version the reader cannot interpret.</summary>
    UnsupportedSchema,
    /// <summary>The manifest, paths, links, or document hashes failed validation.</summary>
    InvalidManifest,
    /// <summary>A required restored package or declared document was not available.</summary>
    MissingAssets
}

/// <summary>The selected assets file, project, target framework, and runtime identifier.</summary>
public sealed record GuidanceScope(string AssetsPath, string ProjectPath, string TargetFramework, string? RuntimeIdentifier);

/// <summary>A portable logical identity; LocalPath is deliberately not part of this key.</summary>
public sealed record GuidanceIdentity(string PackageId, string Version, string PackagePath);

/// <summary>A validated document and its exact package-byte SHA-256.</summary>
public sealed record GuidanceDocument(GuidanceIdentity Identity, string LocalPath, string Sha256, string? Role);

/// <summary>One validated manifest; entry points name document paths exactly as declared.</summary>
public sealed record GuidanceContribution(IReadOnlyList<GuidanceDocument> Documents, IReadOnlyList<string> EntryPoints,
    IReadOnlyDictionary<string, JsonElement> PublisherMetadata);

/// <summary>A package's discovery outcome, retaining machine-local package provenance separately from logical identity.</summary>
public sealed record GuidancePackage(GuidanceScope Scope, string PackageId, string Version, string? PackageRoot,
    GuidanceStatus Status, GuidanceContribution? Contribution, string? Diagnostic)
{
    /// <summary>NuGet's graph-supplied package SHA-512 content hash, when present; not a document hash.</summary>
    public string? NuGetContentHash { get; init; }
}

/// <summary>Graph-wide discovery; consumers must refuse strict operations unless IsSuccessful is true.</summary>
public sealed record GuidanceDiscovery(IReadOnlyList<GuidancePackage> Packages, IReadOnlyList<string> Diagnostics)
{
    /// <summary>True only if assets were readable and every resolved package was valid or had no manifest.</summary>
    public bool IsSuccessful => Diagnostics.Count == 0 &&
        Packages.All(p => p.Status is GuidanceStatus.Valid or GuidanceStatus.NoManifest);
}

/// <summary>Reads only already-restored NuGet assets and verified package payloads; does not run MSBuild or write files.</summary>
public static class GuidanceReader
{
    private const string ManifestPath = "guidance/reference-manifest.json";
    private static readonly StringComparer PortableComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>Discovers every resolved package in each target of the selected assets files, without restore or repository writes.</summary>
    public static GuidanceDiscovery Discover(IEnumerable<string> assetsPaths)
    {
        ArgumentNullException.ThrowIfNull(assetsPaths);
        var packages = new List<GuidancePackage>();
        var diagnostics = new List<string>();
        var selected = false;
        foreach (var assetsPath in assetsPaths)
        {
            selected = true;
            try
            {
                var assetBytes = File.ReadAllBytes(assetsPath);
                using var assets = JsonDocument.Parse(assetBytes.AsMemory(
                    assetBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0));
                ReadAssets(Path.GetFullPath(assetsPath), assets.RootElement, packages, diagnostics);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                diagnostics.Add($"Assets '{assetsPath}': {e.Message}");
            }
        }

        if (!selected)
            diagnostics.Add("No restored NuGet assets files were selected.");

        return new GuidanceDiscovery(packages, diagnostics);
    }

    private static void ReadAssets(string assetsPath, JsonElement root, List<GuidancePackage> packages, List<string> diagnostics)
    {
        if (!Object(root) || !TryObject(root, "targets", out var targets) ||
            !TryObject(root, "libraries", out var libraries) ||
            !TryObject(root, "packageFolders", out var folders) ||
            !TryObject(root, "project", out var project) ||
            !TryObject(project, "restore", out var restore) ||
            !TryString(restore, "projectPath", out var projectPath) ||
            !Path.IsPathFullyQualified(projectPath) || !targets.EnumerateObject().Any() ||
            !folders.EnumerateObject().Any())
        {
            diagnostics.Add($"Assets '{assetsPath}': missing or invalid NuGet graph fields.");
            return;
        }

        var packageFolders = folders.EnumerateObject().Select(p => p.Name).ToArray();
        if (packageFolders.Any(p => !Path.IsPathFullyQualified(p)))
        {
            diagnostics.Add($"Assets '{assetsPath}': package folder is not absolute.");
            return;
        }

        foreach (var target in targets.EnumerateObject())
        {
            if (!Object(target.Value))
            {
                diagnostics.Add($"Assets '{assetsPath}': invalid target '{target.Name}'.");
                continue;
            }

            var slash = target.Name.IndexOf('/');
            var scope = new GuidanceScope(assetsPath, projectPath, slash < 0 ? target.Name : target.Name[..slash],
                slash < 0 ? null : target.Name[(slash + 1)..]);
            foreach (var item in target.Value.EnumerateObject())
            {
                if (!Object(item.Value) || !TryString(item.Value, "type", out var type))
                {
                    diagnostics.Add($"Assets '{assetsPath}': invalid target library '{item.Name}'.");
                    continue;
                }

                if (type != "package")
                    continue;
                if (!libraries.TryGetProperty(item.Name, out var library) || !Object(library) ||
                    !TryString(library, "type", out var libraryType) || libraryType != "package" ||
                    !TryString(library, "path", out var relative) || !SafePath(relative))
                {
                    packages.Add(new GuidancePackage(scope, item.Name, "", null, GuidanceStatus.MissingAssets,
                        null, $"Package '{item.Name}': invalid or missing resolved library path."));
                    continue;
                }

                var index = item.Name.LastIndexOf('/');
                if (index <= 0 || index == item.Name.Length - 1)
                {
                    diagnostics.Add($"Assets '{assetsPath}': invalid package identity '{item.Name}'.");
                    continue;
                }

                var id = item.Name[..index];
                var version = item.Name[(index + 1)..];
                var roots = packageFolders.Select(folder => Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar))).ToArray();
                var packageRoot = roots.FirstOrDefault(Directory.Exists);
                if (packageRoot is null)
                {
                    packages.Add(new GuidancePackage(scope, id, version, roots.FirstOrDefault(),
                        GuidanceStatus.MissingAssets, null, $"Package '{id}/{version}': package cache entry is missing."));
                    continue;
                }

                if (library.TryGetProperty("files", out var declaredFiles) && declaredFiles.ValueKind == JsonValueKind.Array &&
                    declaredFiles.EnumerateArray().Any(file => file.ValueKind == JsonValueKind.String &&
                        file.GetString() == ".nupkg.metadata") &&
                    !File.Exists(Path.Combine(packageRoot, ".nupkg.metadata")))
                {
                    packages.Add(new GuidancePackage(scope, id, version, packageRoot,
                        GuidanceStatus.MissingAssets, null, $"Package '{id}/{version}': NuGet package cache metadata is missing."));
                    continue;
                }

                var manifestListed = library.TryGetProperty("files", out var files) &&
                    files.ValueKind == JsonValueKind.Array &&
                    files.EnumerateArray().Any(file => file.ValueKind == JsonValueKind.String &&
                        string.Equals(file.GetString()?.Replace('\\', '/'), ManifestPath, StringComparison.OrdinalIgnoreCase));
                packages.Add(ReadPackage(scope, id, version, packageRoot, manifestListed) with
                {
                    NuGetContentHash = TryString(library, "sha512", out var contentHash) ? contentHash : null
                });
            }
        }
    }

    private static GuidancePackage ReadPackage(GuidanceScope scope, string id, string version, string packageRoot,
        bool manifestListed)
    {
        GuidancePackage Outcome(GuidanceStatus status, string? diagnostic = null, GuidanceContribution? contribution = null)
            => new(scope, id, version, packageRoot, status, contribution,
                diagnostic is null ? null : $"Package '{id}/{version}': {diagnostic}");

        try
        {
            if (HasLink(packageRoot))
                return Outcome(GuidanceStatus.InvalidManifest, "package root contains a link/reparse point.");
            var manifestFile = Path.Combine(packageRoot, "guidance", "reference-manifest.json");
            if (HasLink(manifestFile))
                return Outcome(GuidanceStatus.InvalidManifest, "manifest path contains a link/reparse point.");
            if (Directory.Exists(manifestFile))
                return Outcome(GuidanceStatus.InvalidManifest, "manifest path is a directory.");
            if (!File.Exists(manifestFile))
                return manifestListed
                    ? Outcome(GuidanceStatus.MissingAssets, "manifest listed by NuGet is missing from the package cache.")
                    : Outcome(GuidanceStatus.NoManifest);

            var manifestBytes = File.ReadAllBytes(manifestFile);
            using var parsed = JsonDocument.Parse(manifestBytes.AsMemory(
                manifestBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0));
            var manifest = parsed.RootElement;
            if (!Object(manifest) || DuplicateProperties(manifest))
                return Outcome(GuidanceStatus.InvalidManifest, "manifest must be an object with unique properties.");
            if (!manifest.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number ||
                !schema.TryGetInt64(out var versionNumber) || versionNumber < 1)
                return Outcome(GuidanceStatus.InvalidManifest, "schemaVersion must be an integer.");
            if (versionNumber != 1)
                return Outcome(GuidanceStatus.UnsupportedSchema, $"unsupported schemaVersion {versionNumber}.");
            if (manifest.EnumerateObject().Any(p => p.Name is not ("schemaVersion" or "documents" or "entryPoints" or "publisherMetadata")) ||
                !TryArray(manifest, "documents", out var rawDocuments) ||
                !TryArray(manifest, "entryPoints", out var rawEntries))
                return Outcome(GuidanceStatus.InvalidManifest, "unknown field or missing documents/entryPoints array.");

            var metadata = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (manifest.TryGetProperty("publisherMetadata", out var extensions))
            {
                if (!Object(extensions) || DuplicateProperties(extensions))
                    return Outcome(GuidanceStatus.InvalidManifest, "publisherMetadata must be an object with unique keys.");
                foreach (var extension in extensions.EnumerateObject())
                {
                    if (!Namespaced(extension.Name))
                        return Outcome(GuidanceStatus.InvalidManifest, $"publisher metadata key '{extension.Name}' is not namespaced.");
                    metadata.Add(extension.Name, extension.Value.Clone());
                }
            }

            var declaredDocuments = new List<(string Path, string Hash, string? Role)>();
            var prefixes = new Dictionary<string, string>(PortableComparer);
            var declared = new HashSet<string>(StringComparer.Ordinal);
            var normalizedDocuments = new HashSet<string>(PortableComparer);
            foreach (var raw in rawDocuments.EnumerateArray())
            {
                if (!Object(raw) || DuplicateProperties(raw) ||
                    raw.EnumerateObject().Any(p => p.Name is not ("path" or "sha256" or "role")) ||
                    !TryString(raw, "path", out var path) || !SafePath(path) ||
                    !TryString(raw, "sha256", out var hash) || hash.Length != 64 ||
                    !hash.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
                    return Outcome(GuidanceStatus.InvalidManifest, "invalid document path, hash, or field.");
                string? role = null;
                if (raw.TryGetProperty("role", out var roleValue))
                {
                    if (roleValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(roleValue.GetString()))
                        return Outcome(GuidanceStatus.InvalidManifest, $"invalid role for '{path}'.");
                    role = roleValue.GetString();
                }

                var parts = path.Replace('\\', '/').Split('/');
                for (var i = 1; i <= parts.Length; i++)
                {
                    var original = string.Join('/', parts.Take(i));
                    var normalized = original.Normalize(NormalizationForm.FormC);
                    if (prefixes.TryGetValue(normalized, out var previous) && previous != original)
                        return Outcome(GuidanceStatus.InvalidManifest, $"portable directory or document alias: '{previous}' and '{original}'.");
                    prefixes[normalized] = original;
                }

                if (!declared.Add(path) ||
                    !normalizedDocuments.Add(path.Replace('\\', '/').Normalize(NormalizationForm.FormC)))
                    return Outcome(GuidanceStatus.InvalidManifest, $"portable document alias: '{path}'.");

                declaredDocuments.Add((path, hash, role));
            }

            if (declaredDocuments.Any(doc => prefixes.Keys.Any(prefix =>
                prefix.StartsWith(doc.Path.Replace('\\', '/').Normalize(NormalizationForm.FormC) + "/", StringComparison.OrdinalIgnoreCase))))
                return Outcome(GuidanceStatus.InvalidManifest, "a document is also used as a directory prefix.");

            var entries = new List<string>();
            foreach (var raw in rawEntries.EnumerateArray())
            {
                if (raw.ValueKind != JsonValueKind.String || raw.GetString() is not { } path ||
                    !declared.Contains(path) || entries.Contains(path, StringComparer.Ordinal))
                    return Outcome(GuidanceStatus.InvalidManifest, "entryPoints must be unique, exact declared document paths.");
                entries.Add(path);
            }

            if (declaredDocuments.Count > 0 && entries.Count == 0)
                return Outcome(GuidanceStatus.InvalidManifest, "non-empty documents require an entry point.");

            var documents = new List<GuidanceDocument>();
            foreach (var (path, hash, role) in declaredDocuments)
            {
                var location = Path.Combine(packageRoot, Path.Combine(path.Replace('\\', '/').Split('/')));
                if (HasLink(location))
                    return Outcome(GuidanceStatus.InvalidManifest, $"document '{path}' contains a link/reparse point.");
                if (!File.Exists(location))
                    return Outcome(GuidanceStatus.MissingAssets, $"document '{path}' is missing.");
                using var stream = File.OpenRead(location);
                var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                if (!string.Equals(hash, actual, StringComparison.Ordinal))
                    return Outcome(GuidanceStatus.InvalidManifest, $"SHA-256 mismatch for '{path}'.");
                documents.Add(new GuidanceDocument(new GuidanceIdentity(id, version, path), location, hash, role));
            }

            return Outcome(GuidanceStatus.Valid, contribution: new GuidanceContribution(documents, entries, metadata));
        }
        catch (JsonException e)
        {
            return Outcome(GuidanceStatus.InvalidManifest, $"malformed manifest: {e.Message}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Outcome(GuidanceStatus.MissingAssets, $"required package asset cannot be read: {e.Message}");
        }
    }

    private static bool HasLink(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return true;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }

            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent == current)
                break;
        }

        return false;
    }

    private static bool SafePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] is '/' or '\\' ||
            path.Any(c => c is < ' ' or '<' or '>' or ':' or '"' or '|' or '?' or '*'))
            return false;
        var segments = path.Replace('\\', '/').Split('/');
        return segments.All(segment => segment.Length > 0 && segment is not ("." or "..") &&
            !segment.EndsWith(' ') && !segment.EndsWith('.') &&
            !ReservedNames.Contains(segment.Split('.')[0]));
    }

    private static bool Namespaced(string key) =>
        key.Length > 2 && key.Contains('.') &&
        key.Split('.').All(p => p.Length > 0 && p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));

    private static bool Object(JsonElement element) => element.ValueKind == JsonValueKind.Object;
    private static bool DuplicateProperties(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().GroupBy(p => p.Name, StringComparer.Ordinal).Any(g => g.Count() > 1) ||
            element.EnumerateObject().Any(p => DuplicateProperties(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Any(DuplicateProperties),
        _ => false
    };
    private static bool TryObject(JsonElement obj, string name, out JsonElement value) =>
        obj.TryGetProperty(name, out value) && Object(value);
    private static bool TryArray(JsonElement obj, string name, out JsonElement value) =>
        obj.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Array;
    private static bool TryString(JsonElement obj, string name, out string value)
    {
        value = "";
        if (!obj.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String ||
            element.GetString() is not { Length: > 0 } text)
            return false;
        value = text;
        return true;
    }
}