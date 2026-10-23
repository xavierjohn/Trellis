namespace Trellis.Core.Tests.Packaging;

public class DocMemberAuditTests(DocMemberAuditTests.CatalogFixture catalogFixture)
    : IClassFixture<DocMemberAuditTests.CatalogFixture>
{
    private const string Source = """
        namespace OwnershipFixtures;
        public class Probe
        {
            public int Code => 1;
            public sealed class Nested
            {
                public int NestedCode => 1;
            }
        }
        public sealed class OtherProbe
        {
            public string ReasonCode => "reason";
        }
        public class ProbeBase
        {
            public int Inherited => 1;
        }
        public sealed class DerivedProbe : ProbeBase { }
        public interface IProbeContract
        {
            int ContractMember { get; }
        }
        public sealed class InterfaceProbe : IProbeContract
        {
            int IProbeContract.ContractMember => 1;
        }
        public sealed class GenericProbe<T>
        {
            public static int Code => 1;
        }
        public static class ProbeExtensions
        {
            public static void OwnedExtension(this Probe probe) { }
            public static void UniversalExtension<T>(this T probe) { }
        }
        public static class AuditSentinel
        {
            public static void Ping() { }
        }
        """;

    private const string Documentation = """
        # Ownership fixtures

        `Probe.Code`, `Probe.Nested`, `Nested.NestedCode`, `OtherProbe.ReasonCode`,
        `ProbeBase.Inherited`, `DerivedProbe.Inherited`, `IProbeContract.ContractMember`,
        `InterfaceProbe.ContractMember`, `GenericProbe<T>.Code`,
        `ProbeExtensions.OwnedExtension`, `ProbeExtensions.UniversalExtension`.

        ```csharp
        AuditSentinel.Ping();
        ```
        """;

    private static readonly (string Markdown, string Receiver)[] s_wrongOwnerCases =
    [
        ("Use `Probe.ReasonCode`.", "Probe"),
        ("### `Probe.ReasonCode`", "Probe"),
        ("| Member | Meaning |\n| --- | --- |\n| `Probe.ReasonCode` | Reason. |", "Probe"),
        ("Use [`Probe.ReasonCode`](reference.md).", "Probe"),
        ("> Use **`Probe.ReasonCode`**.", "Probe"),
        ("Use ``Probe.ReasonCode``.", "Probe"),
        ("Use `OwnershipFixtures.Probe.ReasonCode`.", "Probe"),
        ("Use `GenericProbe<OtherProbe>.ReasonCode`.", "GenericProbe"),
        ("Use `GenericProbe<System.String>.ReasonCode`.", "GenericProbe"),
        ("Use `GenericProbe<GenericProbe<OtherProbe>>.ReasonCode`.", "GenericProbe"),
    ];

    private static readonly string[] s_validOwnerCases =
    [
        "Use `Probe.Code`.",
        "Use `OtherProbe.ReasonCode`.",
        "Use `DerivedProbe.Inherited`.",
        "Use `InterfaceProbe.ContractMember`.",
        "Use `GenericProbe<OtherProbe>.Code`.",
        "Use `Probe.OwnedExtension`.",
        "Use `DerivedProbe.UniversalExtension`.",
        "Use `OwnershipFixtures.Probe.Code`.",
        "Use `Probe.Nested`.",
        "Use `probe.Probe.ReasonCode`.",
        "Use `ReasonCode`.",
        "Use `Probe.ReasonCode()`.",
        "Use `Probe.ReasonCode<T>(value)`.",
        "Use `public string Probe.ReasonCode { get; }`.",
        "Use `Probe.ReasonCode / OtherProbe.ReasonCode`.",
        "Use `\"Probe.ReasonCode\"` as a wire value.",
        "Use `Probe.ReasonCode` <!-- doc-member-ok: Probe.ReasonCode; removed member in historical guidance -->.",
        "No `GenericProbe<OtherProbe>.ReasonCode` <!-- doc-member-ok: GenericProbe.ReasonCode; deliberately absent API --> exists.",
        "Use `Probe.ReasonCode` <!-- doc-member-ok: Probe.ReasonCode; historical GenericProbe&lt;T&gt; member -->.",
    ];

    private static readonly string[] s_markdownTriviaCases =
    [
        "---\ntypes: [`Probe.ReasonCode`]\n---\n",
        "<!-- `Probe.ReasonCode` -->",
        "<!--\nUse `Probe.ReasonCode`.\n-->",
        "<div>\nUse `Probe.ReasonCode`.\n</div>",
        "![`Probe.ReasonCode`](image.png)",
        "Use \\`Probe.ReasonCode\\` literally.",
        "Use `Probe.ReasonCode without closing.",
        "    `Probe.ReasonCode`",
        "```text\nUse `Probe.ReasonCode`.\n```",
        "```json\n{\"member\": \"`Probe.ReasonCode`\"}\n```",
        "```\nUse `Probe.ReasonCode`.\n```",
    ];

    private static readonly string[] s_localTypeCases =
    [
        "class",
        "record",
        "record class",
        "record struct",
        "struct",
        "interface",
        "enum",
    ];

    private static readonly string[] s_invalidExceptionCases =
    [
        "Use `Probe.ReasonCode` <!-- doc-member-ok: OtherProbe.ReasonCode; different receiver -->.",
        "Use `Probe.ReasonCode` <!-- doc-member-ok: Probe.Code; different member -->.",
        "Use `Probe.ReasonCode` <!-- doc-member-ok: Probe.ReasonCode; -->.",
        "Use `Probe.ReasonCode` <!-- doc-member-ok: Probe.ReasonCode;   -->.",
        "Use `Probe.ReasonCode` <!-- doc-member-ok: Probe.ReasonCode; contains < in the reason -->.",
        "Use `Probe.ReasonCode` <!-- doc-member-ok: Probe.ReasonCode; contains > in the reason -->.",
        "Use `Probe.ReasonCode` <!-- doc-member-ok: Probe.ReasonCode; historical GenericProbe<T> member -->.",
        "Use `Probe.ReasonCode` <!-- doc-member-ok: Probe.ReasonCode -->.",
        "Use `Probe.ReasonCode` <!-- v1-stale-ok: historical reference -->.",
        "<!-- doc-member-ok: Probe.ReasonCode; misplaced marker -->\nUse `Probe.ReasonCode`.",
        "Use `Probe.ReasonCode`.\n<!-- doc-member-ok: Probe.ReasonCode; marker on another line -->",
        "Use `Probe.ReasonCode` and this comment <!-- doc-member-ok: Probe.ReasonCode; not adjacent -->.",
    ];

    public static TheoryData<string, string> WrongOwnerCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (markdown, receiver) in s_wrongOwnerCases)
            data.Add(markdown, receiver);
        return data;
    }

    public static TheoryData<string> ValidOwnerCases() => new(s_validOwnerCases);

    public static TheoryData<string> MarkdownTriviaCases() => new(s_markdownTriviaCases);

    public static TheoryData<string> LocalTypeCases() => new(s_localTypeCases);

    public static TheoryData<string> InvalidExceptionCases() => new(s_invalidExceptionCases);

    [Theory]
    [MemberData(nameof(WrongOwnerCases))]
    public void Audit_StandaloneInline_WrongOwner_FailsEvenWhenTheMemberExistsElsewhere(string markdown, string receiver)
    {
        var result = RunAudit(markdown);

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain($"error TRLDOC014: '{receiver}.ReasonCode' does not resolve");
    }

    [Theory]
    [MemberData(nameof(ValidOwnerCases))]
    public void Audit_InlineReference_ValidOwnerOrDeliberateBoundary_Passes(string markdown)
    {
        var result = RunAudit(markdown);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().NotContain("error TRLDOC014");
    }

    [Theory]
    [MemberData(nameof(MarkdownTriviaCases))]
    public void Audit_MarkdownTriviaOrNonCSharpCode_InlineLookalike_IsNotAnOwnershipClaim(string markdown)
    {
        var result = RunAudit(markdown, prepend: true);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().NotContain("error TRLDOC014");
    }

    [Theory]
    [MemberData(nameof(LocalTypeCases))]
    public void Audit_DocumentLocalTypeDeclaration_InlineReference_ShadowsTheAssemblyType(string declaration)
    {
        var result = RunAudit(LocalTypeMarkdown(declaration));

        result.ExitCode.Should().Be(0, result.Output);
    }

    [Theory]
    [MemberData(nameof(InvalidExceptionCases))]
    public void Audit_InlineException_InvalidOrUnscopedMarker_DoesNotSuppressOwnership(string markdown)
    {
        var result = RunAudit(markdown);

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("error TRLDOC014: 'Probe.ReasonCode' does not resolve");
    }

    [Fact]
    public async Task Audit_ReusedCatalog_DocumentAndAssemblyInputs_RemainIsolated()
    {
        DocMemberAudit.Catalog originalCatalog;
        using (var original = new DocumentationAuditFixture())
        {
            original.WritePackage("Primary", Source, Documentation);
            originalCatalog = original.CreateMemberCatalog("Primary");
        }

        DocMemberAudit.Catalog revisedCatalog;
        using (var revised = new DocumentationAuditFixture())
        {
            revised.WritePackage("Primary", "public sealed class Probe { public int ReasonCode => 1; }", "`Probe.ReasonCode`");
            revisedCatalog = revised.CreateMemberCatalog("Primary");
        }

        using var waived = new DocumentationAuditFixture();
        waived.WriteDocument("waived.md", "Use `Probe.ReasonCode`.");
        waived.WriteLedger("doc-only-members.txt", "Probe.ReasonCode");
        using var strict = new DocumentationAuditFixture();
        strict.WriteDocument("strict.md", "Use `Probe.ReasonCode`.");

        var results = await Task.WhenAll(
            Task.Run(() => waived.RunMemberAudit(originalCatalog), TestContext.Current.CancellationToken),
            Task.Run(() => strict.RunMemberAudit(originalCatalog), TestContext.Current.CancellationToken),
            Task.Run(() => strict.RunMemberAudit(revisedCatalog), TestContext.Current.CancellationToken));

        results[0].ExitCode.Should().Be(0, results[0].Output);
        results[0].Output.Should().NotContain("strict.md");
        results[1].ExitCode.Should().Be(1, results[1].Output);
        results[1].Output.Should().Contain("strict.md(1,1): error TRLDOC014: 'Probe.ReasonCode' does not resolve");
        results[1].Output.Should().NotContain("waived.md");
        results[2].ExitCode.Should().Be(0, results[2].Output);

        var repeated = strict.RunMemberAudit(originalCatalog);
        repeated.ExitCode.Should().Be(1, repeated.Output);
        repeated.Output.Should().Be(results[1].Output);
    }

    [Fact]
    public async Task Audit_GrammarCorpus_ScopedCatalog_MatchesFullCliDiagnostics()
    {
        using var fixture = new DocumentationAuditFixture();
        fixture.WritePackage("Primary", Source, Documentation);
        var documents = GrammarDocuments().ToArray();
        documents.Should().HaveCount(59);
        for (var i = 0; i < documents.Length; i++)
            fixture.WriteDocument($"grammar-{i:D2}.md", documents[i]);

        var expected = fixture.RunMemberAudit(fixture.CreateMemberCatalog("Primary"));
        var actual = await fixture.RunAsync();

        actual.ExitCode.Should().Be(expected.ExitCode, actual.Output);
        OwnershipDiagnostics(actual.Output).Should().Equal(OwnershipDiagnostics(expected.Output));
        actual.Output.Should().NotContain("error TRLDOC005");
        actual.Output.Should().NotContain("error TRLDOC008");
        actual.Output.Should().NotContain("error TRLDOC015");
    }

    [Fact]
    public async Task Audit_InlineException_LaterIdenticalClaim_StillFailsAtItsOwnLine()
    {
        var documentation = Documentation + "\r\n\r\n" + """
            No `Probe.ReasonCode` <!-- doc-member-ok: Probe.ReasonCode; removed API --> exists.

            Use `Probe.ReasonCode`.
            """;
        using var fixture = new DocumentationAuditFixture();
        fixture.WritePackage("Primary", Source, documentation);

        var result = await fixture.RunAsync();
        var expectedLine = documentation[..documentation.LastIndexOf("Use `Probe.ReasonCode`", StringComparison.Ordinal)].Count(c => c == '\n') + 1;

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain($"trellis-api-primary.md({expectedLine},1): error TRLDOC014: 'Probe.ReasonCode' does not resolve");
    }

    [Fact]
    public async Task Audit_NeighborDocument_ExampleType_DoesNotShadowTheOwningDocument()
    {
        using var fixture = new DocumentationAuditFixture();
        fixture.WritePackage("Primary", Source, Documentation + "\n\nUse `Probe.ReasonCode`.");
        fixture.WriteDocument("example.md", "```csharp\npublic class Probe { }\n```\n\n`Probe.ReasonCode`");

        var result = await fixture.RunAsync();

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("trellis-api-primary.md");
        result.Output.Should().NotContain("example.md(");
    }

    [Fact]
    public async Task Audit_ReceiverQualifiedAllowlist_LegitimateShorthand_Passes()
    {
        using var fixture = new DocumentationAuditFixture();
        fixture.WritePackage("Primary", Source, Documentation + "\n\nUse `Probe.ReasonCode`.");
        fixture.WriteLedger("doc-only-members.txt", "Probe.ReasonCode");

        var result = await fixture.RunAsync();

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().NotContain("no longer referenced");
    }

    [Fact]
    public async Task Audit_BareSymbolAllowlist_WrongOwner_DoesNotSuppressOwnership()
    {
        using var fixture = new DocumentationAuditFixture();
        fixture.WritePackage("Primary", Source, Documentation + "\n\nUse `Probe.ReasonCode`.");
        fixture.WriteLedger("doc-only-symbols.txt", "ReasonCode");

        var result = await fixture.RunAsync();

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("error TRLDOC014: 'Probe.ReasonCode' does not resolve");
        result.Output.Should().NotContain("error TRLDOC005");
    }

    [Theory]
    [InlineData("Use `Probe.ReasonCode`.")]
    [InlineData("```csharp\nProbe.ReasonCode;\n```")]
    [InlineData("```csharp\nProbe.ReasonCode; // <!-- doc-member-ok: Probe.ReasonCode; never waives compiled code -->\n```")]
    public async Task Audit_AmbiguousMemberLedger_AttributedButWrongOwner_StillFailsOwnership(string markdown)
    {
        using var fixture = new DocumentationAuditFixture();
        fixture.WritePackage("Primary", Source, Documentation + "\n\n" + markdown);
        fixture.WriteLedger("ambiguous-members.txt", "ReasonCode");

        var result = await fixture.RunAsync();

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("error TRLDOC014: 'Probe.ReasonCode' does not resolve");
        result.Output.Should().NotContain("error TRLDOC015");
    }

    [Fact]
    public async Task Audit_AmbiguousMemberLedger_BareMember_RemainsAnAttributionFailure()
    {
        using var fixture = new DocumentationAuditFixture();
        fixture.WritePackage("Primary", Source, Documentation + "\n\nUse `ReasonCode`.");
        fixture.WriteLedger("ambiguous-members.txt", "ReasonCode");

        var result = await fixture.RunAsync();

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("error TRLDOC015");
        result.Output.Should().NotContain("error TRLDOC014");
    }

    [Fact]
    public async Task Audit_InlineOnly_OwnedMember_ProvidesNonVacuousCoverage()
    {
        using var fixture = new DocumentationAuditFixture();
        fixture.WritePackage("Primary", "public sealed class Probe { public int Code => 1; }", "`Probe.Code`");

        var result = await fixture.RunAsync();

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("C# fences scanned: 0");
        result.Output.Should().Contain("receiver-qualified pairs checked: 1");
    }

    [Fact]
    public async Task Audit_NoQualifiedMembers_VacuityGuard_StillFails()
    {
        using var fixture = new DocumentationAuditFixture();
        fixture.WritePackage("Primary", "public sealed class Probe { public int Code => 1; }", "### Probe\n\n`Code`");

        var result = await fixture.RunAsync();

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("error TRLDOC014: Extracted no receiver-qualified member accesses");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Audit_DuplicateDependencyBuild_ExtensionResolution_UsesTheSelectedCurrentAssembly(bool wrongOwner, bool copiedIntoConsumer)
    {
        using var fixture = new DocumentationAuditFixture();
        fixture.WritePackage("Trellis.Dependency", """
            namespace Dependency;
            public sealed class Probe { public int Code => 1; }
            public sealed class OtherProbe { public int StaleCode => 1; }
            """, "`Probe.Code`, `OtherProbe.StaleCode`, `Probe.OwnedExtension`"
                + (wrongOwner ? "\n\nUse `Probe.StaleCode`." : ""));
        fixture.WriteBuild("Trellis.Dependency", """
            namespace Dependency;
            public sealed class Probe { public int StaleCode => 1; }
            public sealed class OtherProbe { public int StaleCode => 1; }
            """, "Debug");
        fixture.WritePackage("Trellis.Consumer", """
            using Dependency;
            public static class ConsumerExtensions
            {
                public static void OwnedExtension(this Probe probe) { }
            }
            """, "`ConsumerExtensions.OwnedExtension`", "Trellis.Dependency");

        if (copiedIntoConsumer)
            fixture.MoveBuildToPackage("Trellis.Dependency", "Debug", "Trellis.Consumer");

        var result = await fixture.RunAsync();

        result.ExitCode.Should().Be(wrongOwner ? 1 : 0, result.Output);
        result.Output.Should().NotContain("'Probe.OwnedExtension' does not resolve");
        result.Output.Should().NotContain("error TRLDOC005");
        result.Output.Should().NotContain("error TRLDOC008");
        if (wrongOwner)
            result.Output.Should().Contain("error TRLDOC014: 'Probe.StaleCode' does not resolve");
    }

    private (int ExitCode, string Output) RunAudit(string markdown, bool prepend = false)
    {
        using var fixture = new DocumentationAuditFixture();
        fixture.WriteDocument("trellis-api-primary.md", WithDocumentation(markdown, prepend));
        return fixture.RunMemberAudit(catalogFixture.Catalog);
    }

    private static IEnumerable<string> GrammarDocuments() =>
        s_wrongOwnerCases.Select(row => WithDocumentation(row.Markdown))
            .Concat(s_validOwnerCases.Select(markdown => WithDocumentation(markdown)))
            .Concat(s_markdownTriviaCases.Select(markdown => WithDocumentation(markdown, prepend: true)))
            .Concat(s_localTypeCases.Select(declaration => WithDocumentation(LocalTypeMarkdown(declaration))))
            .Concat(s_invalidExceptionCases.Select(markdown => WithDocumentation(markdown)));

    private static string WithDocumentation(string markdown, bool prepend = false) =>
        prepend ? markdown + "\n\n" + Documentation : Documentation + "\n\n" + markdown;

    private static string LocalTypeMarkdown(string declaration) => $$"""
        Use `Probe.ReasonCode` on this example-owned type.

        ```csharp
        public {{declaration}} Probe { }
        ```
        """;

    private static IEnumerable<string> OwnershipDiagnostics(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Contains(": error TRLDOC014:", StringComparison.Ordinal));

    public sealed class CatalogFixture
    {
        internal DocMemberAudit.Catalog Catalog { get; }

        public CatalogFixture()
        {
            using var fixture = new DocumentationAuditFixture();
            fixture.WritePackage("Primary", Source, Documentation);
            Catalog = fixture.CreateMemberCatalog("Primary");
        }
    }
}