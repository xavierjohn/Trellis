namespace Trellis.Analyzers.Tests;

using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Tests for <see cref="ProducesClobbersProblemDetailsAnalyzer"/> (TRLS065).
/// <para>
/// The rule reports a <c>[Produces]</c> whose media types include a JSON-family type, because
/// <c>ProducesAttribute</c> rewrites <c>ObjectResult.ContentTypes</c> wholesale and the JSON
/// output formatter will then write a <c>ProblemDetails</c> as the listed type instead of
/// <c>application/problem+json</c>. Non-JSON media types are deliberately silent: their
/// formatters decline <c>ProblemDetails</c>, so MVC falls back and the problem keeps its media
/// type. Both facts are pinned by behavioural tests in <c>Trellis.Asp.Tests</c>.
/// </para>
/// </summary>
public sealed class ProducesClobbersProblemDetailsAnalyzerTests
{
    /// <summary>
    /// Minimal MVC + Trellis.Asp surface. The Trellis.Asp type is what gates the rule: the
    /// analyzer stays silent in a compilation that does not reference Trellis.Asp, because
    /// [Produces] is a stock MVC attribute and Trellis has no standing to judge its use there.
    /// </summary>
    private const string StubSource = """
        namespace Microsoft.AspNetCore.Mvc
        {
            using System;

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
            public class ProducesAttribute : Attribute
            {
                public ProducesAttribute(string contentType, params string[] additionalContentTypes) { }
                public ProducesAttribute(Type type) { }
            }

            public class ControllerBase { }

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class ApiControllerAttribute : Attribute { }
        }

        namespace Trellis.Asp
        {
            public sealed class TrellisAspOptions { }
        }
        """;

    private static async Task VerifyAsync(string source, string mediaType, bool successClobbered = false)
    {
        var expected = AnalyzerTestHelper
            .Diagnostic(DiagnosticDescriptors.ProducesClobbersProblemDetails)
            .WithLocation(0)
            .WithArguments(
                mediaType,
                successClobbered
                    ? "MVC will write successful ObjectResult responses as application/problem+json"
                    : "MVC will write RFC 9457 problem responses as that media type instead of application/problem+json");

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ProducesClobbersProblemDetailsAnalyzer>(source, expected);
        test.TestState.Sources.Add(("Stubs.cs", StubSource));
        await test.RunAsync();
    }

    private static async Task VerifyNoDiagnosticAsync(string source, bool includeTrellisAsp = true)
    {
        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<ProducesClobbersProblemDetailsAnalyzer>(source);
        test.TestState.Sources.Add(("Stubs.cs", includeTrellisAsp
            ? StubSource
            : StubSource.Replace("public sealed class TrellisAspOptions { }", "public sealed class SomethingElse { }")));
        await test.RunAsync();
    }

    [Fact]
    public async Task Json_only_Produces_on_a_controller_is_reported()
    {
        // The originally reported shape: narrowing to application/json rewrites every
        // ObjectResult problem -- including the [ApiController] automatic 400/422 -- from
        // application/problem+json to application/json.
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            [{|#0:Produces("application/json")|}]
            public sealed class OrdersController : ControllerBase { }
            """;

        await VerifyAsync(source, "application/json");
    }

    [Fact]
    public async Task Action_level_Produces_is_reported()
    {
        // ProducesAttribute is the same result filter wherever it is declared.
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            public sealed class OrdersController : ControllerBase
            {
                [{|#0:Produces("application/json")|}]
                public void Get() { }
            }
            """;

        await VerifyAsync(source, "application/json");
    }

    [Fact]
    public async Task Appending_problem_json_is_still_reported()
    {
        // Formatter selection follows list order, so problem+json anywhere but first is inert.
        // A rule keyed on "omits problem+json" would go green here -- which is exactly why this
        // analyzer is not keyed that way.
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            [{|#0:Produces("application/json", "application/problem+json")|}]
            public sealed class OrdersController : ControllerBase { }
            """;

        await VerifyAsync(source, "application/json");
    }

    [Fact]
    public async Task Prepending_problem_json_is_still_reported()
    {
        // Prepending repairs the problem response but rewrites plain ObjectResult SUCCESS
        // responses to application/problem+json, so it trades one defect for another.
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            [{|#0:Produces("application/problem+json", "application/json")|}]
            public sealed class OrdersController : ControllerBase { }
            """;

        await VerifyAsync(source, "application/problem+json", successClobbered: true);
    }

    [Fact]
    public async Task Vendor_json_suffix_is_reported()
    {
        // SystemTextJsonOutputFormatter advertises application/*+json, so it happily writes a
        // ProblemDetails as application/vnd.contoso+json -- same defect, different spelling.
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            [{|#0:Produces("application/vnd.contoso+json")|}]
            public sealed class OrdersController : ControllerBase { }
            """;

        await VerifyAsync(source, "application/vnd.contoso+json");
    }

    [Fact]
    public async Task Media_type_parameters_do_not_hide_the_json_subtype()
    {
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            [{|#0:Produces("application/json; charset=utf-8")|}]
            public sealed class OrdersController : ControllerBase { }
            """;

        await VerifyAsync(source, "application/json; charset=utf-8");
    }

    [Fact]
    public async Task Csv_only_Produces_is_not_reported()
    {
        // Measured: a text/csv formatter's CanWriteType rejects ProblemDetails, so MVC falls
        // back and the problem response keeps application/problem+json. Serving CSV is safe.
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            public sealed class ReportsController : ControllerBase
            {
                [Produces("text/csv")]
                public void Download() { }
            }
            """;

        await VerifyNoDiagnosticAsync(source);
    }

    [Fact]
    public async Task Pdf_and_xml_Produces_are_not_reported()
    {
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            public sealed class ReportsController : ControllerBase
            {
                [Produces("application/pdf", "application/xml")]
                public void Download() { }
            }
            """;

        await VerifyNoDiagnosticAsync(source);
    }

    [Fact]
    public async Task Type_only_Produces_overload_is_not_reported()
    {
        // ProducesAttribute(Type) sets the declared response type and leaves ContentTypes empty,
        // so it cannot rewrite anything.
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            public sealed record OrderDto(int Id);

            [ApiController]
            public sealed class OrdersController : ControllerBase
            {
                [Produces(typeof(OrderDto))]
                public void Get() { }
            }
            """;

        await VerifyNoDiagnosticAsync(source);
    }

    [Fact]
    public async Task Produces_is_not_reported_when_the_project_does_not_reference_Trellis_Asp()
    {
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            [Produces("application/json")]
            public sealed class OrdersController : ControllerBase { }
            """;

        await VerifyNoDiagnosticAsync(source, includeTrellisAsp: false);
    }

    [Fact]
    public async Task Problem_json_after_a_non_json_type_is_still_reported()
    {
        // ["text/csv", "application/problem+json"] LOOKS like it declares both outcomes
        // correctly. Measured behaviour says otherwise: MVC loops over formatters in the outer
        // loop, so the JSON formatter matches the problem+json entry before the CSV formatter is
        // ever consulted, and the success response ships as problem+json. Pinned by
        // ProducesAttributeContentTypeTests.Non_json_type_followed_by_problem_json_still_rewrites_the_success_response.
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            public sealed class ReportsController : ControllerBase
            {
                [{|#0:Produces("text/csv", "application/problem+json")|}]
                public void Export() { }
            }
            """;

        await VerifyAsync(source, "application/problem+json", successClobbered: true);
    }

    [Fact]
    public async Task Plain_json_after_a_non_json_type_is_still_reported()
    {
        // Contrast with the row above: application/json is not problem+json, so the failure is
        // still rewritten once the CSV formatter declines it.
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            public sealed class ReportsController : ControllerBase
            {
                [{|#0:Produces("text/csv", "application/json")|}]
                public void Export() { }
            }
            """;

        await VerifyAsync(source, "application/json");
    }

    [Fact]
    public async Task Named_constructor_argument_is_read()
    {
        // NameColon is a named constructor argument, not a property initialiser -- skipping it
        // would silently miss the plainest possible spelling of the defect.
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            [{|#0:Produces(contentType: "application/json")|}]
            public sealed class OrdersController : ControllerBase { }
            """;

        await VerifyAsync(source, "application/json");
    }

    [Fact]
    public async Task Explicit_array_for_the_params_parameter_is_read()
    {
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            [{|#0:Produces("text/csv", new[] { "application/json" })|}]
            public sealed class OrdersController : ControllerBase { }
            """;

        await VerifyAsync(source, "application/json");
    }

    [Fact]
    public async Task Reordered_named_arguments_are_read_in_parameter_order()
    {
        // Source order puts problem+json first, but the constructor sees text/csv first. Both
        // orders happen to be reported here; what the test pins is the reported media type, which
        // would be "text/csv" -- a type this rule never reports -- if arguments were read in
        // source order rather than parameter order.
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            [{|#0:Produces(additionalContentTypes: new[] { "application/problem+json" }, contentType: "text/csv")|}]
            public sealed class ReportsController : ControllerBase { }
            """;

        await VerifyAsync(source, "application/problem+json", successClobbered: true);
    }

    [Fact]
    public async Task Json_subtype_under_a_foreign_top_level_type_is_not_reported()
    {
        // SystemTextJsonOutputFormatter advertises application/*+json, and text/vnd.contoso+json
        // is not a subset of that wildcard -- different top-level type. The JSON formatter
        // declines it, so the clobbering mechanism does not apply and the rule must stay silent.
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            public sealed class ReportsController : ControllerBase
            {
                [Produces("text/vnd.contoso+json")]
                public void Export() { }
            }
            """;

        await VerifyNoDiagnosticAsync(source);
    }

    [Fact]
    public async Task Text_json_is_reported()
    {
        // text/json IS advertised explicitly, unlike other text/* JSON spellings.
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            [ApiController]
            [{|#0:Produces("text/json")|}]
            public sealed class OrdersController : ControllerBase { }
            """;

        await VerifyAsync(source, "text/json");
    }

    [Fact]
    public async Task Const_media_type_is_resolved_and_reported()
    {
        const string source = """
            using Microsoft.AspNetCore.Mvc;

            public static class Media
            {
                public const string Json = "application/json";
            }

            [ApiController]
            [{|#0:Produces(Media.Json)|}]
            public sealed class OrdersController : ControllerBase { }
            """;

        await VerifyAsync(source, "application/json");
    }
}
