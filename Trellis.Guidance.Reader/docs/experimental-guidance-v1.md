# Experimental NuGet package guidance contract, version 1

This is an experimental convention and reference implementation, **not** a NuGet standard.
Package authors place `guidance/reference-manifest.json` in the resolved package. No build
target, NuGet client extension, or Trellis dependency is required. The machine-readable
[JSON Schema](reference-manifest-v1.schema.json) describes the manifest structure;
the additional semantic constraints below are normative.

## Manifest

The UTF-8 JSON object requires `schemaVersion: 1`, `documents` and `entryPoints` arrays.
No duplicate JSON property names or unknown top-level/document fields are permitted.
Each document declares `path` and the lowercase hexadecimal SHA-256 of its **exact
package file bytes** (including any BOM and line endings). `role` is an optional,
nonempty descriptive string; no role is required to participate. At least one entry
point is required if documents are nonempty. Every entry point must equal a declared
document path exactly; duplicate entry points or document paths are invalid. Empty
arrays represent a valid metadata-only contribution. `publisherMetadata`, if present,
is an object with reverse-domain-style dot-separated ASCII keys (such as
`org.example`); values are arbitrary JSON and have no generic routing semantics.
For example, Trellis's publisher profile uses `publisherMetadata["org.trellis"]`
with `lockstepCohort` (Core) or `lockstep` (satellites). Generic readers treat
both keys as opaque metadata, not as a rule imposed on unrelated publishers.
Packages may publish a single short guide. No TFM/RID routing exists in version 1.

Document paths are package-relative and can be nested; accepted spelling is retained
for link resolution. `/` and `\` separate segments. Reject absolute or drive-rooted
paths, empty/`.`/`..` segments, control characters, Windows-forbidden characters
`<>:"|?*`, trailing spaces/dots and Windows device-name segments (including extensions).
Never follow symlinks or reparse points in the package root, manifest or document paths.
Resolve documents only beneath the package root named by the restored assets graph.
Within **one** package contribution, replace `\` with `/`, normalize each prefix to
Unicode NFC and compare ordinally without case to reject distinct spellings of the
same document **or directory prefix**. A document cannot also be a directory prefix.
The same filename in separate package/version namespaces is fine.

The portable identity of a document is `(resolved package ID, resolved version,
declared package-relative path)`. The package ID/version and package location come
from NuGet's restored graph, never publisher metadata. A local full path is only a
machine-local location; it must not be committed as a portable identifier.

## Discovery and errors

Call `GuidanceReader.Discover(IEnumerable<string> assetsPaths)` with already-restored
`obj/project.assets.json` paths. For each selected project's target, the reader uses
`targets` (package entries only), `libraries` (`path`) and `packageFolders` to
find payloads, preserving the project path, framework and runtime identifier in
`GuidanceScope`. It neither evaluates MSBuild nor restores/builds, executes package
targets, consults Git, nor writes repository files. A caller selecting multiple
projects supplies multiple assets paths. Graph selection and freshness comparison of
project restore specifications are the consumer's responsibility. If the NuGet graph's
package file list names `.nupkg.metadata`, its absence counts as an incomplete cache
entry rather than as a package that opted out of guidance. Likewise, a listed
`guidance/reference-manifest.json` that is absent from the package cache is
`MissingAssets`, not `NoManifest`.

`GuidanceDiscovery.Packages` retains every package's scope, identity, cache root,
optional NuGet graph SHA-512 content hash, status and diagnostic. `Valid` includes a `GuidanceContribution` of entry points,
verified documents, local paths, roles and publisher metadata. `NoManifest` means
an existing package has no manifest and is not an error. `UnsupportedSchema`,
`InvalidManifest` (including malformed JSON, unsafe paths, duplicate aliases and
hash mismatches), and `MissingAssets` (absent cache entries or listed documents)
are failures. Missing/malformed assets files and incomplete graph records surface
in `GuidanceDiscovery.Diagnostics`; inspect them as well as package diagnostics.
`IsSuccessful` is false if either kind of failure occurs, even when other packages
validated; strict operations must reject the **whole** discovery result. This reader
does not authorize executing document instructions or materialize any output.

Conformance examples and negative fixtures are in `..\tests\Fixtures\`; the test
project exercises them against synthetic already-restored NuGet graphs. Fixture
Markdown is stored without Git line-ending conversion so its declared byte hashes
remain valid on Windows and Linux. Future schema
versions require an explicit compatibility decision rather than silently accepting
version-1-incompatible selectors.
