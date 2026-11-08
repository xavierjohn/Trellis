using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>The documents of the API reference: its Markdown files plus the Agent Skills in its skills subfolder.</summary>
internal static class ReferenceDocuments
{
    public static IEnumerable<string> Enumerate(string docsDir)
    {
        var skills = Path.Combine(docsDir, "skills");

        return Directory.EnumerateFiles(docsDir, "*.md")
            .Concat(Directory.Exists(skills) ? Directory.EnumerateFiles(skills, "*.md") : [])
            .OrderBy(p => p, StringComparer.Ordinal);
    }
}
