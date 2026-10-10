namespace Trellis.Docs.Audit;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

internal sealed class ApiDocumentationEvidence
{
    private static readonly MarkdownPipeline s_pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UsePipeTables()
        .UseAutoLinks()
        .Build();

    private static readonly Regex s_identifier = new(@"\b\w+\b", RegexOptions.Compiled);

    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    public ApiDocumentationEvidence(string markdown) =>
        CollectBlocks(Markdown.Parse(markdown.TrimStart('\uFEFF'), s_pipeline));

    public bool Contains(string name) => _names.Contains(name);

    private void CollectBlocks(ContainerBlock blocks, bool structured = false)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case FencedCodeBlock fence when IsCSharp(fence.Info):
                    AddCode(fence.Lines.ToString());
                    break;
                case CodeBlock:
                    break;
                case ContainerBlock container:
                    CollectBlocks(container, structured || container is TableCell);
                    break;
                case LeafBlock leaf:
                    var text = structured || leaf is HeadingBlock ? new StringBuilder() : null;
                    CollectInlines(leaf.Inline, text);
                    if (text is not null)
                        AddIdentifiers(text.ToString());
                    break;
            }
        }
    }

    private void CollectInlines(ContainerInline? inlines, StringBuilder? text)
    {
        if (inlines is null)
            return;

        for (var inline = inlines.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case CodeInline code:
                    AddCode(code.Content);
                    text?.Append(' ');
                    break;
                case LinkInline { IsImage: true }:
                case LinkInline { IsAutoLink: true }:
                case AutolinkInline:
                    text?.Append(' ');
                    break;
                case ContainerInline container:
                    CollectInlines(container, text);
                    break;
                case LiteralInline literal:
                    text?.Append(literal.Content.ToString());
                    break;
                case HtmlEntityInline entity:
                    text?.Append(entity.Transcoded.ToString());
                    break;
                case LineBreakInline:
                    text?.Append(' ');
                    break;
            }
        }
    }

    private void AddCode(string code) => AddIdentifiers(CSharpCode.BlankCommentsAndLiterals(code));

    private void AddIdentifiers(string text)
    {
        foreach (Match match in s_identifier.Matches(text))
            _names.Add(match.Value);
    }

    private static bool IsCSharp(string? language) =>
        string.Equals(language, "csharp", StringComparison.OrdinalIgnoreCase)
        || string.Equals(language, "c#", StringComparison.OrdinalIgnoreCase)
        || string.Equals(language, "cs", StringComparison.OrdinalIgnoreCase);
}