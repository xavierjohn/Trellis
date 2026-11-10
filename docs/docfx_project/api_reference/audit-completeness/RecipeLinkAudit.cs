namespace Trellis.Docs.Audit;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

/// <summary>One violation found by <see cref="RecipeLinkAudit"/>.</summary>
internal sealed record RecipeLinkFinding(string Rule, int Line, string Message);

/// <summary>
/// Recipe cross-reference gate (TRLDOC019 and TRLDOC020).
///
/// A recipe is loaded on demand, so an agent holding one recipe cannot resolve "Recipe 4" (the skill name is
/// <c>recipe-04-slug</c>), and an agent holding one part of a split recipe has no route to the other part unless
/// the part links to it. Both rules turn on one question, "is this really a link?", and the answer is the
/// Markdown parser's, not a regular expression's: whether <c>[text][ref]</c> resolves, whether a code span or an
/// HTML comment hides link syntax, and whether a definition is a definition are all CommonMark rules that DocFX
/// applies when it renders the page. This audit asks the same parser (Markdig, the one DocFX uses), so it cannot
/// disagree with the rendered site about what is a link.
///
/// TRLDOC019: a mention of another recipe by number ("Recipe 21") is the label of a real link to that recipe's file
/// (the number in <c>recipe-21-slug.md</c> must match the number in the label). A number in inline
/// code, an HTML comment, a code fence or the YAML front matter is not a link and so is rejected (inside a fence,
/// name the recipe file stem instead). A recipe's own number, its level-1 title and the separate Microservices
/// cookbook are exempt.
///
/// TRLDOC020: the documents of a recipe split across several files (the same recipe number, such as the two parts
/// of recipe 13) each link to every sibling.
/// </summary>
internal static class RecipeLinkAudit
{
    // The Markdig extensions DocFX 2.80.1 enables in UseDocfxExtensions (docfx.json sets no markdigExtensions), so the
    // audit parses text the way the site renders it: ~~21~~ is strikethrough, $21$ is math. DocFX's own extensions
    // (xref, include, tabs, containers) add no recipe-number text and are not reproduced. Front matter is Markdig's,
    // and source locations are precise so a finding names its line.
    private static readonly MarkdownPipeline s_pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UseMathematics()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseMediaLinks()
        .UsePipeTables()
        .UseAutoLinks()
        .UseEmojiAndSmiley(false)
        .UsePreciseSourceLocation()
        .Build();

    private static readonly Regex s_recipeFile = new(@"^recipe-(?<number>\d+)-", RegexOptions.Compiled);
    // The start of a raw <img> tag, and one attribute of it at a time: a name, then optionally = and a value that is
    // double-quoted, single-quoted or a bare word. Reading attributes in order is what keeps data-alt, a quoted > and
    // text inside another attribute's value from being taken for the alt attribute.
    private static readonly Regex s_imgTag = new(@"^<img(?=[\s/>])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex s_attribute = new(
        @"\G[\s/]*(?<name>[^\s""'<>/=]+)(?:\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s""'=<>`]+)))?",
        RegexOptions.Compiled);

    private static readonly Regex s_mention = new(@"Recipes?\s+(?<number>\d+)", RegexOptions.Compiled);

    public static int Run(string docsDir)
    {
        Console.WriteLine();
        Console.WriteLine("=== Recipe cross-reference audit (TRLDOC019, TRLDOC020) ===");

        var recipeFiles = Directory.Exists(Path.Combine(docsDir, "recipes"))
            ? Directory.EnumerateFiles(Path.Combine(docsDir, "recipes"), "recipe-*.md").OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : [];

        // Like the other audits, refuse to pass by checking nothing.
        if (recipeFiles.Length == 0)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{Path.Combine(docsDir, "recipes")}(1,1): error TRLDOC019: no recipe documents were found, so this gate would enforce nothing."));
            return 1;
        }

        int failures = 0;
        int links = 0;

        foreach (var file in recipeFiles)
        {
            var name = Path.GetFileName(file);
            var number = RecipeNumber(name);
            var siblings = recipeFiles
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(other => other != name && RecipeNumber(other) == number)
                .ToArray();

            var findings = Check(name, File.ReadAllText(file), siblings, out var linkCount);
            links += linkCount;

            foreach (var finding in findings)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{file}({finding.Line},1): error {finding.Rule}: {finding.Message}"));
                failures++;
            }
        }

        if (links == 0)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{Path.Combine(docsDir, "recipes")}(1,1): error TRLDOC019: no Markdown links were found in {recipeFiles.Length} recipe documents, so the parser is not seeing the documents. Fix the audit rather than passing it."));
            return 1;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Scanned {recipeFiles.Length} recipe documents and {links} links; {failures} violation(s)."));
        return failures == 0 ? 0 : 1;
    }

    public static IReadOnlyList<RecipeLinkFinding> Check(string fileName, string markdown, IReadOnlyCollection<string> siblingFileNames) =>
        Check(fileName, markdown, siblingFileNames, out _);

    public static IReadOnlyList<RecipeLinkFinding> Check(
        string fileName, string markdown, IReadOnlyCollection<string> siblingFileNames, out int linkCount)
    {
        var text = markdown.TrimStart('﻿');
        var sourceLines = text.Split('\n');
        var document = Markdown.Parse(text, s_pipeline);
        var ownNumber = RecipeNumber(fileName);
        var findings = new List<RecipeLinkFinding>();

        // Images are not navigation, and neither is a link written inside an image's description: DocFX renders that as
        // plain alt text. Every other LinkInline is a link the parser resolved (inline, reference, shortcut or autolink).
        // An unresolved reference is plain text and never appears here.
        var links = document.Descendants<LinkInline>().Where(link => !link.IsImage && !IsInsideImage(link)).ToList();
        linkCount = links.Count;

        // Text is read as a reader sees it, not as the parser splits it: Recipe **21** is the text "Recipe 21" even though
        // the parser makes a literal and an emphasis of it. Every text node that goes into a flattened text is recorded
        // here so it is scanned once, as part of that text.
        var flattened = new HashSet<MarkdownObject>(ReferenceEqualityComparer.Instance);
        var labelOf = links.ToDictionary(link => link, link => Flatten(link, flattened, throughImages: true));

        void Scan(string raw, Func<int, int> zeroBasedLineAt, bool codeLike, LinkInline? owner = null)
        {
            foreach (Match mention in s_mention.Matches(raw))
            {
                var line = Math.Clamp(zeroBasedLineAt(mention.Index), 0, sourceLines.Length - 1);

                if (sourceLines[line].Contains("microservices cookbook", StringComparison.OrdinalIgnoreCase))
                    continue;

                var mentioned = int.Parse(mention.Groups["number"].Value, CultureInfo.InvariantCulture);

                if (mentioned == ownNumber)
                    continue;

                // A label that names a recipe must open that recipe: the number in the target's recipe-NN-slug.md
                // file name is the evidence, so [Recipe 21](recipe-20-x.md) is rejected like a bare mention.
                if (owner is not null)
                {
                    var path = LinkedFile(owner.Url);

                    // A relative file link only: an absolute URL can end in a matching file name and still open elsewhere.
                    if (!Uri.TryCreate(path, UriKind.Absolute, out _) && RecipeNumber(Path.GetFileName(path)) == mentioned)
                        continue;

                    findings.Add(new RecipeLinkFinding("TRLDOC019", line + 1,
                        $"'{mention.Value}' is the label of a link to '{owner.Url}', which is not recipe {mentioned}; point the link at the file of recipe {mentioned} (recipe-{mentioned:00}-name.md)."));
                    continue;
                }

                var fix = codeLike
                    ? "name the recipe file stem (for example recipe-02-command-handler)"
                    : "make it the label of a Markdown link, as [Recipe N](recipe-NN-name.md)";

                findings.Add(new RecipeLinkFinding("TRLDOC019", line + 1,
                    $"'{mention.Value}' is a bare recipe number that an agent holding only this recipe cannot resolve; {fix}, and say in a clause what the other recipe supplies."));
            }
        }

        // A mention counts only inside the label of a link, so each label is scanned as a whole.
        foreach (var (link, label) in labelOf)
        {
            if (ParentBlockOf(link) is not HeadingBlock { Level: 1 })
                Scan(label.Text, offset => label.Lines[offset], codeLike: false, link);
        }

        // Prose outside any label is scanned across its formatting too, so See Recipe **21** is the bare mention that
        // DocFX renders without a link. A link or an image inside it is a boundary: text on either side does not join.
        foreach (var block in document.Descendants<LeafBlock>())
        {
            if (block.Inline is null || block is HeadingBlock { Level: 1 })
                continue;

            var prose = Flatten(block.Inline, flattened, throughImages: false);
            Scan(prose.Text, offset => prose.Lines[offset], codeLike: false);
        }

        // The alt text of an image inside a link is part of that link's label, flattened above, and so is the alt text of
        // an image inside another image. Any other image's alt text is unlinked: ![Recipe **21**](x.png) shows "Recipe 21"
        // with no link, so it is read across its formatting like the prose around it.
        foreach (var image in document.Descendants<LinkInline>().Where(link => link.IsImage && !IsInsideLink(link)))
        {
            if (ParentBlockOf(image) is HeadingBlock { Level: 1 })
                continue;

            var alt = Flatten(image, flattened, throughImages: true);
            Scan(alt.Text, offset => alt.Lines[offset], codeLike: false);
        }

        // What no flattened text holds: the tag text of raw inline HTML, which is not a label.
        foreach (var inline in document.Descendants<Inline>())
        {
            var raw = inline switch
            {
                LiteralInline literal => literal.Content.ToString(),
                CodeInline code => code.Content,
                HtmlEntityInline entity => entity.Transcoded.ToString(),
                HtmlInline html => html.Tag,
                _ => null,
            };

            if (raw is null || flattened.Contains(inline) || ParentBlockOf(inline) is HeadingBlock { Level: 1 })
                continue;

            Scan(raw, _ => inline.Line, codeLike: false);
        }

        // Text the parser keeps as lines: code fences and indented code, YAML front matter and HTML blocks (including
        // comments). None of it can hold a link.
        foreach (var leaf in document.Descendants<LeafBlock>())
        {
            if (leaf.Inline is not null || leaf.Lines.Lines is null)
                continue;

            var codeLike = leaf is CodeBlock or YamlFrontMatterBlock;

            for (var i = 0; i < leaf.Lines.Count; i++)
            {
                var line = leaf.Lines.Lines[i];
                Scan(line.Slice.ToString(), _ => line.Line, codeLike);
            }
        }

        var linkedFiles = links
            .Select(link => LinkedFile(link.Url))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var sibling in siblingFileNames.OrderBy(s => s, StringComparer.Ordinal))
        {
            if (linkedFiles.Contains(sibling))
                continue;

            findings.Add(new RecipeLinkFinding("TRLDOC020", 1,
                $"'{fileName}' shares recipe number {ownNumber} with '{sibling}' but does not link to it. Link every sibling document of a split recipe, as [Recipe {ownNumber} part N]({sibling}), so an agent holding one part can find the others."));
        }

        return findings.OrderBy(f => f.Line).ThenBy(f => f.Rule, StringComparer.Ordinal).ToList();
    }

    // A document's text in reading order, with the source line of each character. Literals and code spans contribute
    // their text, character references are decoded, emphasis is looked through, and a line break reads as a space. A link or an image is a boundary (text on
    // either side must not join into a mention) unless throughImages reads an image's alt text as part of the text, as a
    // link label does. Raw HTML contributes nothing and is left to the caller's own scan.
    // Each text node used is added to consumed.
    private static (string Text, IReadOnlyList<int> Lines) Flatten(
        ContainerInline root, HashSet<MarkdownObject> consumed, bool throughImages)
    {
        var text = new StringBuilder();
        var lines = new List<int>();
        Append(root);
        return (text.ToString(), lines);

        void Add(string value, int line)
        {
            text.Append(value);
            lines.AddRange(Enumerable.Repeat(line, value.Length));
        }

        void Append(ContainerInline container)
        {
            foreach (var child in container)
            {
                switch (child)
                {
                    case LiteralInline literal:
                        Add(literal.Content.ToString(), child.Line);
                        consumed.Add(child);
                        break;
                    case CodeInline code:
                        Add(code.Content, child.Line);
                        consumed.Add(child);
                        break;
                    case MathInline math:
                        // $21$ renders as the numeral 21.
                        Add(math.Content.ToString(), child.Line);
                        consumed.Add(child);
                        break;
                    case HtmlEntityInline entity:
                        // &nbsp; and &#49; read as the character they decode to, which is what the page shows.
                        Add(entity.Transcoded.ToString(), child.Line);
                        consumed.Add(child);
                        break;
                    case LineBreakInline:
                        Add(" ", child.Line);
                        break;
                    case HtmlInline html when throughImages && ImgAlt(html.Tag) is { } alt:
                        // A raw <img> in a label or in alt text is an image like ![alt](src): its alt text is read as text.
                        Add(alt, child.Line);
                        consumed.Add(child);
                        break;
                    case LinkInline link when (throughImages && link.IsImage) || IsInsideImage(link):
                        // Alt text, however it is written: the words of a link inside it are text, not a link.
                        Append(link);
                        break;
                    case LinkInline:
                        Add("\u0001", child.Line);
                        break;
                    case ContainerInline nested:
                        Append(nested);
                        break;
                }
            }
        }
    }

    // The alt text of a raw <img> tag with character references decoded, or null when the tag is not an <img> or has no
    // alt attribute. As in a browser, the first alt attribute wins and attribute names are not case sensitive.
    private static string? ImgAlt(string tag)
    {
        if (!s_imgTag.IsMatch(tag))
            return null;

        var position = "<img".Length;

        while (s_attribute.Match(tag, position) is { Success: true } attribute)
        {
            if (string.Equals(attribute.Groups["name"].Value, "alt", StringComparison.OrdinalIgnoreCase))
                return WebUtility.HtmlDecode(attribute.Groups["v"].Value);

            position = attribute.Index + attribute.Length;
        }

        return null;
    }

    // Whether a link sits inside an image's description, where DocFX renders it as plain alt text.
    private static bool IsInsideImage(LinkInline link)
    {
        for (var parent = link.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is LinkInline { IsImage: true })
                return true;
        }

        return false;
    }

    // Whether a link or image sits inside another link or image, so its text is part of that one's text.
    private static bool IsInsideLink(LinkInline link)
    {
        for (var parent = link.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is LinkInline)
                return true;
        }

        return false;
    }

    // An inline's block is recorded on the root of its inline tree.
    private static LeafBlock? ParentBlockOf(Inline inline)
    {
        ContainerInline? container = inline.Parent;

        while (container?.Parent is not null)
            container = container.Parent;

        return container?.ParentBlock;
    }

    private static int RecipeNumber(string fileName)
    {
        var match = s_recipeFile.Match(fileName);
        return match.Success ? int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture) : -1;
    }

    // The file a link points at: its target without any #anchor or query, and without a leading ./
    private static string LinkedFile(string? url)
    {
        var target = url ?? string.Empty;
        var end = target.IndexOfAny(['#', '?']);

        if (end >= 0)
            target = target[..end];

        return target.StartsWith("./", StringComparison.Ordinal) ? target[2..] : target;
    }
}
