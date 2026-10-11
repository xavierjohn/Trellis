namespace Trellis.Core.Tests.Packaging;

public class DocMemberAuditTests
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

    [Theory]
    [InlineData("Use `Probe.ReasonCode`.", "Probe")]
    [InlineData("### `Probe.ReasonCode`", "Probe")]
    [InlineData("| Member | Meaning |\n| --- | --- |\n| `Probe.ReasonCode` | Reason. |", "Probe")]
    [InlineData("Use [`Probe.ReasonCode`](reference.md).", "Probe")]
    [InlineData("> Use **`Probe.ReasonCode`**.", "Probe")]
    [InlineData("Use ``Probe.ReasonCode``.", "Probe")]
    [InlineData("Use `OwnershipFixtures.Probe.ReasonCode`.", "Probe")]
    [InlineData("Use `GenericProbe<OtherProbe>.ReasonCode`.", "GenericProbe")]
    [InlineData("Use `GenericProbe<System.String>.ReasonCode`.", "GenericProbe")]
    [InlineData("Use `GenericProbe<GenericProbe<OtherProbe>>.ReasonCode`.", "GenericProbe")]
    public async Task Audit_StandaloneInline_WrongOwner_FailsEvenWhenTheMemberExistsElsewhere(string markdown, string receiver)
    {
        var result = await RunAuditAsync(markdown);

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain($"error TRLDOC014: '{receiver}.ReasonCode' does not resolve");
        result.Output.Should().NotContain("error TRLDOC005");
        result.Output.Should().NotContain("error TRLDOC008");
        result.Output.Should().NotContain("error TRLDOC015");
    }

    [Theory]
    [InlineData("Use `Probe.Code`.")]
    [InlineData("Use `OtherProbe.ReasonCode`.")]
    [InlineData("Use `DerivedProbe.Inherited`.")]
    [InlineData("Use `InterfaceProbe.ContractMember`.")]
    [InlineData("Use `GenericProbe<OtherProbe>.Code`.")]
    [InlineData("Use `Probe.OwnedExtension`.")]
    [InlineData("Use `DerivedProbe.UniversalExtension`.")]
    [InlineData("Use `OwnershipFixtures.Probe.Code`.")]
    [InlineData("Use `Probe.Nested`.")]
    [InlineData("Use `probe.Probe.ReasonCode`.")]
    [InlineData("Use `ReasonCode`.")]
    [InlineData("Use `Probe.ReasonCode()`.")]
    [InlineData("Use `Probe.ReasonCode<T>(value)`.")]
    [InlineData("Use `public string Probe.ReasonCode { get; }`.")]
    [InlineData("Use `Probe.ReasonCode / OtherProbe.ReasonCode`.")]
    [InlineData("Use `\"Probe.ReasonCode\"` as a wire value.")]
    [InlineData("Use `Probe.ReasonCode` <!-- doc-member-ok: Probe.ReasonCode; removed member in historical guidance -->.")]
    [InlineData("No `GenericProbe<OtherProbe>.ReasonCode` <!-- doc-member-ok: GenericProbe.ReasonCode; deliberately absent API --> exists.")]
    public async Task Audit_InlineReference_ValidOwnerOrDeliberateBoundary_Passes(string markdown)
    {
        var result = await RunAuditAsync(markdown);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().NotContain("error TRLDOC014");
    }

    [Theory]
    [InlineData("---\ntypes: [`Probe.ReasonCode`]\n---\n")]
    [InlineData("<!-- `Probe.ReasonCode` -->")]
    [InlineData("<!--\nUse `Probe.ReasonCode`.\n-->")]
    [InlineData("<div>\nUse `Probe.ReasonCode`.\n</div>")]
    [InlineData("![`Probe.ReasonCode`](image.png)")]
    [InlineData("Use \\`Probe.ReasonCode\\` literally.")]
    [InlineData("Use `Probe.ReasonCode without closing.")]
    [InlineData("    `Probe.ReasonCode`")]
    [InlineData("```text\nUse `Probe.ReasonCode`.\n```")]
    [InlineData("```json\n{\"member\": \"`Probe.ReasonCode`\"}\n```")]
    [InlineData("```\nUse `Probe.ReasonCode`.\n```")]
    public async Task Audit_MarkdownTriviaOrNonCSharpCode_InlineLookalike_IsNotAnOwnershipClaim(string markdown)
    {
        var result = await RunAuditAsync(markdown, prepend: true);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().NotContain("error TRLDOC014");
    }

    [Theory]
    [InlineData("class")]
    [InlineData("record")]
    [InlineData("record class")]
    [InlineData("record struct")]
    [InlineData("struct")]
    [InlineData("interface")]
    [InlineData("enum")]
    public async Task Audit_DocumentLocalTypeDeclaration_InlineReference_ShadowsTheAssemblyType(string declaration)
    {
        var result = await RunAuditAsync($$"""
            Use `Probe.ReasonCode` on this example-owned type.

            ```csharp
            public {{declaration}} Probe { }
            ```
            """);

        result.ExitCode.Should().Be(0, result.Output);
    }

    [Theory]
    [InlineData("Use `Probe.ReasonCode` <!-- doc-member-ok: OtherProbe.ReasonCode; different receiver -->.")]
    [InlineData("Use `Probe.ReasonCode` <!-- doc-member-ok: Probe.Code; different member -->.")]
    [InlineData("Use `Probe.ReasonCode` <!-- doc-member-ok: Probe.ReasonCode; -->.")]
    [InlineData("Use `Probe.ReasonCode` <!-- doc-member-ok: Probe.ReasonCode;   -->.")]
    [InlineData("Use `Probe.ReasonCode` <!-- doc-member-ok: Probe.ReasonCode -->.")]
    [InlineData("Use `Probe.ReasonCode` <!-- v1-stale-ok: historical reference -->.")]
    [InlineData("<!-- doc-member-ok: Probe.ReasonCode; misplaced marker -->\nUse `Probe.ReasonCode`.")]
    [InlineData("Use `Probe.ReasonCode`.\n<!-- doc-member-ok: Probe.ReasonCode; marker on another line -->")]
    [InlineData("Use `Probe.ReasonCode` and this comment <!-- doc-member-ok: Probe.ReasonCode; not adjacent -->.")]
    public async Task Audit_InlineException_MismatchedOrUnscopedMarker_DoesNotSuppressOwnership(string markdown)
    {
        var result = await RunAuditAsync(markdown);

        result.ExitCode.Should().Be(1, result.Output);
        result.Output.Should().Contain("error TRLDOC014: 'Probe.ReasonCode' does not resolve");
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Audit_DuplicateDependencyBuild_ExtensionResolution_UsesTheSelectedCurrentAssembly(bool wrongOwner)
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

        var result = await fixture.RunAsync();

        result.ExitCode.Should().Be(wrongOwner ? 1 : 0, result.Output);
        result.Output.Should().NotContain("'Probe.OwnedExtension' does not resolve");
        result.Output.Should().NotContain("error TRLDOC005");
        result.Output.Should().NotContain("error TRLDOC008");
        if (wrongOwner)
            result.Output.Should().Contain("error TRLDOC014: 'Probe.StaleCode' does not resolve");
    }

    private static async Task<(int ExitCode, string Output)> RunAuditAsync(string markdown, bool prepend = false)
    {
        using var fixture = new DocumentationAuditFixture();
        fixture.WritePackage("Primary", Source, prepend
            ? markdown + "\n\n" + Documentation
            : Documentation + "\n\n" + markdown);
        return await fixture.RunAsync();
    }
}