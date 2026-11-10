namespace Trellis.Core.Tests.Packaging;

using Trellis.Docs.Audit;

/// <summary>
/// Negative and positive controls for <see cref="RecipeLinkAudit"/>. Each case is a document shape a review once
/// disagreed about; the parser (Markdig, the one DocFX renders with) is the oracle, so a case is accepted exactly when
/// the rendered page would contain a real link.
/// </summary>
public class RecipeLinkAuditTests
{
    private const string Own = "recipe-05-example.md";
    private const string Part = "recipe-13-part-two.md";
    private const string Sibling = "recipe-13-part-one.md";

    // TRLDOC019: a mention of Recipe 21 (not this recipe's own number) that is the label of a real link.
    [Theory]
    [InlineData("See [Recipe 21](recipe-21-x.md).")]
    // A label the parser splits into several inlines is still one label.
    [InlineData("See [Recipe **21**](recipe-21-x.md).")]
    [InlineData("See [**Recipe 21**](recipe-21-x.md).")]
    [InlineData("See [Recipe *21*](recipe-21-x.md).")]
    [InlineData("See [Recipe `21`](recipe-21-x.md).")]
    [InlineData("See [Recipe\n21](recipe-21-x.md).")]
    [InlineData("See [Recipe 4](recipe-04-x.md).")]
    [InlineData("See [Recipe 21](../recipes/recipe-21-x.md#heading).")]
    [InlineData("See [Recipe 4][r].\n\n[r]: recipe-04-x.md")]
    [InlineData("See [Recipe 21](recipe-21-x.md \"Title\").")]
    [InlineData("See [Recipe 21](./recipe-21-x.md#section).")]
    [InlineData("See [Recipe 21](<recipe-21-x.md>).")]
    [InlineData("See [`Recipe 21`](recipe-21-x.md).")]
    [InlineData("See [the Recipe 21 guide](recipe-21-x.md).")]
    [InlineData("See [Recipe 21][r].\n\n[r]: recipe-21-x.md")]
    [InlineData("See [Recipe 21][R].\n\n[r]: recipe-21-x.md")]
    [InlineData("See [Recipe 21][].\n\n[Recipe  21]: recipe-21-x.md")]
    [InlineData("See [Recipe 21].\n\n[recipe 21]: recipe-21-x.md")]
    [InlineData("See [`Recipe 21`].\n\n[`Recipe 21`]: recipe-21-x.md")]
    [InlineData("See [`Recipe 21`][].\n\n[`Recipe 21`]: recipe-21-x.md")]
    [InlineData("| A | B |\n| --- | --- |\n| x | [Recipe 21](recipe-21-x.md) |")]
    [InlineData("# Recipe 5 — This recipe\n\nBody.")]
    [InlineData("This recipe is Recipe 5 and says so.")]
    // A link written inside an image description is alt text, not a link, so its words are not a mention that needs one.
    [InlineData("![See [diagram](x.md)](d.svg)")]
    // The alt text of an image in a link is part of that link's label.
    [InlineData("See [![Recipe 21](x.png)](recipe-21-x.md).")]
    [InlineData("See [![Recipe **21**](x.png)](recipe-21-x.md).")]
    [InlineData("See [Recipe ![21](x.svg)](recipe-21-x.md).")]
    [InlineData("See [![Recipe&nbsp;21](x.png)][r].\n\n[r]: recipe-21-x.md")]
    [InlineData("![diagram](x.png)")]
    [InlineData("See [![diagram](x.png)](recipe-21-x.md).")]
    // Strikethrough and math are parsed as DocFX parses them.
    [InlineData("See [Recipe ~~21~~](recipe-21-x.md).")]
    [InlineData("See [Recipe $21$](recipe-21-x.md).")]
    [InlineData("This recipe is Recipe ~~5~~ and says so.")]
    // A character reference decodes to the character the page shows.
    [InlineData("See [Recipe 2&#49;](recipe-21-x.md).")]
    [InlineData("See [Recipe&nbsp;21](recipe-21-x.md).")]
    [InlineData("See [Recipe&#32;21](recipe-21-x.md).")]
    [InlineData("This recipe is Recipe&nbsp;5 and says so.")]
    [InlineData("This recipe is **Recipe 5** and says so.")]
    [InlineData("# Recipe **21** — a heading")]
    // A link between the words is a boundary, not a mention that joins them.
    [InlineData("Recipe [x](recipe-21-x.md) 21 items")]
    [InlineData("The Microservices cookbook has its own Recipe 4.")]
    [InlineData("```csharp\n// see recipe-21-x for the handler\n```")]
    public void Mention_IsRealLink_OrExempt_IsAccepted(string markdown) =>
        RecipeLinkAudit.Check(Own, markdown, []).Should().BeEmpty();

    // TRLDOC019: the same mention, but what looks like a link is not one.
    [Theory]
    [InlineData("See Recipe 21.")]
    // A link inside an image description is not a link: its label is unlinked alt text.
    [InlineData("![See [Recipe 21](recipe-21-x.md)](d.svg)")]
    [InlineData("![See [Recipe 21](recipe-20-x.md)](d.svg)")]
    [InlineData("![See [Recipe **21**](recipe-21-x.md)](d.svg)")]
    [InlineData("![See [Recipe 21][r]](d.svg)\n\n[r]: recipe-21-x.md")]
    [InlineData("See [Recipe ![[21](recipe-21-x.md)](d.svg)](recipe-20-x.md).")]
    // The alt text of an image in a link names a recipe the link does not open.
    [InlineData("See [![Recipe 21](x.png)](recipe-20-x.md).")]
    [InlineData("See [Recipe ![21](x.svg)](recipe-20-x.md).")]
    [InlineData("See [![Recipe **21**](x.png)](recipe-20-x.md).")]
    [InlineData("See [Recipe ![2**1**](x.svg)](recipe-20-x.md).")]
    [InlineData("See [Recipe ![21](x.svg)](https://example.com/recipe-21-x.md).")]
    [InlineData("See [Recipe ![21](x.svg)][r].\n\n[r]: recipe-20-x.md")]
    [InlineData("See [![Recipe 21](x.png)](#top).")]
    [InlineData("See ![Recipe ![21](x.svg)](y.png).")]
    // DocFX renders ~~21~~ as struck-through text and $21$ as math: the text still reads "Recipe 21".
    [InlineData("See [Recipe ~~21~~](recipe-20-x.md).")]
    [InlineData("See [Recipe $21$](recipe-20-x.md).")]
    [InlineData("See Recipe ~~21~~.")]
    [InlineData("See ~~Recipe~~ 21.")]
    [InlineData("See Recipe $21$.")]
    // An image's alt text is unlinked text, however it is written.
    [InlineData("![Recipe 21](x.png)")]
    [InlineData("![Recipe **21**](x.png)")]
    [InlineData("![Recipe&nbsp;21](x.png)")]
    [InlineData("![Recipe 2&#49;](x.png)")]
    [InlineData("![**Recipe** *21*](x.png)")]
    [InlineData("![Recipe ~~21~~](x.png)")]
    [InlineData("![Recipe `21`](x.png)")]
    [InlineData("![Recipe\n21](x.png)")]
    [InlineData("![Recipe **21**][i].\n\n[i]: x.png")]
    // The same decoding, for text that is not a link.
    [InlineData("See Recipe&nbsp;21.")]
    [InlineData("See Recipe 2&#49;.")]
    [InlineData("See Recipe&#x20;21.")]
    [InlineData("See **Recipe**&nbsp;**21**.")]
    [InlineData("See [Recipe&nbsp;21](recipe-20-x.md).")]
    [InlineData("See [Recipe 2&#49;](recipe-20-x.md).")]
    [InlineData("See [Recipe&#32;21][r].\n\n[r]: recipe-20-x.md")]
    // Unlinked prose is read across its formatting, as the rendered page shows it.
    [InlineData("See Recipe **21**.")]
    [InlineData("See **Recipe** 21.")]
    [InlineData("See *Recipe* **21**.")]
    [InlineData("See Recipe *21*.")]
    [InlineData("See Recipe 2**1**.")]
    [InlineData("See Recipe `21`.")]
    [InlineData("See Recipe\n**21**.")]
    [InlineData("See Recipe <b>21</b>.")]
    [InlineData("## Recipe **21**")]
    [InlineData("- Recipe **21**")]
    [InlineData("> Recipe **21**")]
    [InlineData("| A | B |\n| --- | --- |\n| x | Recipe **21** |")]
    // A split label that names the wrong recipe.
    [InlineData("See [Recipe **21**](recipe-20-x.md).")]
    [InlineData("See [**Recipe 21**](recipe-20-x.md).")]
    [InlineData("See [Recipe *21*](recipe-20-x.md).")]
    [InlineData("See [Recipe `21`](recipe-20-x.md).")]
    [InlineData("See [Recipe 2**1**](recipe-20-x.md).")]
    [InlineData("See [Recipe\n21](recipe-20-x.md).")]
    [InlineData("See [Recipe **21**][r].\n\n[r]: recipe-20-x.md")]
    [InlineData("See [Recipe **21**](https://example.com/recipe-21-x.md).")]
    [InlineData("See **Recipe 21**.")]
    // The label names a recipe the link does not open.
    [InlineData("See [Recipe 21](recipe-20-x.md).")]
    [InlineData("See [the Recipe 21 guide](recipe-22-x.md).")]
    [InlineData("See [Recipe 21](recipe-210-x.md).")]
    [InlineData("See [`Recipe 21`](recipe-20-x.md).")]
    [InlineData("See [Recipe 21](#section).")]
    [InlineData("See [Recipe 21](other.md).")]
    [InlineData("See [Recipe 21](https://example.com/recipe-21-x.md).")]
    [InlineData("See [Recipe 21][r].\n\n[r]: recipe-20-x.md")]
    [InlineData("See [Recipe 21].\n\n[recipe 21]: recipe-20-x.md")]
    [InlineData("| A | B |\n| --- | --- |\n| x | [Recipe 21](recipe-20-x.md) |")]
    [InlineData("See Recipes 21 and 22.")]
    [InlineData("Use `Recipe 21`.")]
    [InlineData("Use `Recipe 21` and [something else](x.md).")]
    [InlineData("See `[Recipe 21](recipe-21-x.md)`.")]
    [InlineData("See ``[Recipe 21](recipe-21-x.md)``.")]
    [InlineData("See `[Recipe 21][r]`.\n\n[r]: recipe-21-x.md")]
    [InlineData("See `[Recipe 21]`.\n\n[recipe 21]: recipe-21-x.md")]
    [InlineData("<!-- See [Recipe 21](recipe-21-x.md) -->")]
    [InlineData("<!--\n[Recipe 21](recipe-21-x.md)\n-->")]
    [InlineData("<!-- note --> [Recipe 21](recipe-21-x.md)")]
    [InlineData("See [Recipe 21][missing].")]
    [InlineData("See [Recipe 21][].")]
    [InlineData("See [Recipe 21].")]
    [InlineData("See [Recipe 21][r].\n\n```\n[r]: recipe-21-x.md\n```")]
    [InlineData("See [Recipe 21][r].\n\n<!--\n[r]: recipe-21-x.md\n-->")]
    [InlineData("See [Recipe 21][r].\n\n    [r]: recipe-21-x.md")]
    [InlineData("![Recipe 21](diagram.png)")]
    [InlineData("```csharp\n// see Recipe 21 for the handler\n```")]
    [InlineData("```\n<!-- opens a comment inside a fence\n```\n\nThen Recipe 21.")]
    [InlineData("Use `<!--` literally, then Recipe 21.")]
    [InlineData("---\nagent_description: \"Uses Recipe 21.\"\n---\n# Recipe 5 — Title")]
    [InlineData("| A | B |\n| --- | --- |\n| x | Recipe 21 |")]
    // The recipe 13 part two defect: an unpaired backtick opens a code span that swallows the link after it.
    [InlineData("Odd ticks: `a`b`c, see [Recipe 21](recipe-21-x.md) and `d`.")]
    [InlineData("| A | B |\n| --- | --- |\n| `a`b`c | see [Recipe 21](recipe-21-x.md) and `d` |")]
    public void Mention_NotARealLink_IsRejected(string markdown) =>
        RecipeLinkAudit.Check(Own, markdown, []).Should().Contain(f => f.Rule == "TRLDOC019");

    // The line of a mention split by formatting is the line its first word is on.
    [Fact]
    public void Mention_SplitByFormatting_ReportsItsLine()
    {
        var findings = RecipeLinkAudit.Check(Own, "# Recipe 5 — Title\n\nFirst line.\nSee Recipe\n**21**.\n\nSee **Recipe** *22*.", []);

        findings.Select(f => (f.Rule, f.Line)).Should().Equal(("TRLDOC019", 4), ("TRLDOC019", 7));
    }

    [Fact]
    public void Mention_ReportsItsLine()
    {
        var findings = RecipeLinkAudit.Check(Own, "# Recipe 5 — Title\n\nSome text.\n\nSee Recipe 21 here.\n\n```csharp\nvar x = 1; // Recipe 22\n```", []);

        findings.Select(f => (f.Rule, f.Line)).Should().Equal(("TRLDOC019", 5), ("TRLDOC019", 8));
    }

    // TRLDOC020: this part links to its sibling.
    [Theory]
    [InlineData("Continues [part one](recipe-13-part-one.md).")]
    [InlineData("Continues [part one](./recipe-13-part-one.md#heading \"Title\").")]
    [InlineData("Continues [part one](<recipe-13-part-one.md>).")]
    [InlineData("Continues [`part one`](recipe-13-part-one.md).")]
    [InlineData("Continues [part one][p1].\n\n[p1]: recipe-13-part-one.md")]
    [InlineData("Continues [part one][].\n\n[Part One]: recipe-13-part-one.md#heading")]
    [InlineData("Continues [part one].\n\n[part one]: recipe-13-part-one.md")]
    public void Sibling_IsLinked_IsAccepted(string markdown) =>
        RecipeLinkAudit.Check(Part, markdown, [Sibling]).Should().BeEmpty();

    // TRLDOC020: the sibling's file name appears, but not as a link.
    [Theory]
    [InlineData("Nothing links anywhere.")]
    [InlineData("See recipe-13-part-one.md in the folder.")]
    [InlineData("<!-- [part one](recipe-13-part-one.md) -->")]
    [InlineData("<!--\n[part one](recipe-13-part-one.md)\n-->")]
    [InlineData("<!-- note --> [part one](recipe-13-part-one.md)")]
    [InlineData("`[part one](recipe-13-part-one.md)`")]
    [InlineData("``[part one](recipe-13-part-one.md)``")]
    [InlineData("`[part one][p1]`\n\n[p1]: recipe-13-part-one.md")]
    [InlineData("```csharp\n// [part one](recipe-13-part-one.md)\n```")]
    [InlineData("[part one](recipe-13-part-one.md.bak)")]
    [InlineData("[part one](other/recipe-13-part-one.md)")]
    [InlineData("[part one][missing]")]
    [InlineData("[part one][p1]\n\n```\n[p1]: recipe-13-part-one.md\n```")]
    [InlineData("[part one]\n\n<!--\n[part one]: recipe-13-part-one.md\n-->")]
    [InlineData("A definition nothing uses.\n\n[p1]: recipe-13-part-one.md")]
    [InlineData("![part one](recipe-13-part-one.md)")]
    // A sibling linked only from inside an image description is not linked.
    [InlineData("![See [part one](recipe-13-part-one.md)](d.svg)")]
    [InlineData("![[part one](recipe-13-part-one.md)](d.svg)")]
    [InlineData("![See [part one][p1]](d.svg)\n\n[p1]: recipe-13-part-one.md")]
    [InlineData("[![[part one](recipe-13-part-one.md)](d.svg)](other.md)")]
    public void Sibling_NotLinked_IsRejected(string markdown) =>
        RecipeLinkAudit.Check(Part, markdown, [Sibling]).Should().Contain(f => f.Rule == "TRLDOC020");

    [Fact]
    public void Siblings_EachMustBeLinked()
    {
        var findings = RecipeLinkAudit.Check(
            "recipe-13-part-three.md",
            "Continues [part one](recipe-13-part-one.md).",
            ["recipe-13-part-one.md", "recipe-13-part-two.md"]);

        findings.Should().ContainSingle().Which.Message.Should().Contain("recipe-13-part-two.md");
    }

    [Fact]
    public void Document_WithNoSiblings_NeedsNoLinks() =>
        RecipeLinkAudit.Check(Own, "Plain text with no recipe mentions.", []).Should().BeEmpty();

    [Fact]
    public void ByteOrderMark_DoesNotShiftTheFindings() =>
        RecipeLinkAudit.Check(Own, "﻿See Recipe 21.", []).Should().ContainSingle().Which.Line.Should().Be(1);
}
