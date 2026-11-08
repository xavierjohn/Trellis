using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>The documents of the API reference: its Markdown files plus the recipe documents in its recipes subfolder.</summary>
internal static class ReferenceDocuments
{
    public static IEnumerable<string> Enumerate(string docsDir)
    {
        var recipes = Path.Combine(docsDir, "recipes");

        return Directory.EnumerateFiles(docsDir, "*.md")
            .Concat(Directory.Exists(recipes) ? Directory.EnumerateFiles(recipes, "*.md") : [])
            .OrderBy(p => p, StringComparer.Ordinal);
    }
}
