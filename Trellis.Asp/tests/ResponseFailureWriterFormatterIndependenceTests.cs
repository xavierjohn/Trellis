namespace Trellis.Asp.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Failure responses are JSON regardless of what the client asks for and what output formatters the
/// application registered.
/// <para>
/// Success responses have always been JSON-only by construction — <c>ToHttpResponse</c> returns an
/// <c>IResult</c> that writes its own body and never consults a formatter. Failures used to be the
/// asymmetric case, through two independent doors: <c>ResponseFailureWriter</c> reached MVC content
/// negotiation via <c>IProblemDetailsService</c>, and <c>ScalarValueValidationFilter</c> reached it
/// via <c>ProblemDetailsActionResult</c>'s inner <c>ObjectResult</c>. Either way an
/// <c>XmlDataContractSerializerOutputFormatter</c> would try to render a <c>ProblemDetails</c>
/// whose <c>Extensions</c> hold <c>FieldViolationProblemDetail</c> values, and throw. These tests
/// pin the symmetry on both paths, and pin that closing them did not cost the ProblemDetails
/// customization pipeline.
/// </para>
/// </summary>
public sealed class ResponseFailureWriterFormatterIndependenceTests
{
    public enum XmlFormatter
    {
        None,
        DataContract,
        XmlSerializer,
    }

    private static IHost CreateHost(
        XmlFormatter xml = XmlFormatter.None,
        bool trellisProblemDetails = false,
        Action<Microsoft.AspNetCore.Http.ProblemDetailsContext>? customize = null)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    if (trellisProblemDetails)
                        s.AddTrellisProblemDetails();
                    else
                        s.AddProblemDetails();

                    if (customize is not null)
                        s.AddProblemDetails(o => o.CustomizeProblemDetails = customize);

                    var mvc = s.AddControllers().AddApplicationPart(typeof(DiagController).Assembly);
                    switch (xml)
                    {
                        case XmlFormatter.DataContract:
                            mvc.AddXmlDataContractSerializerFormatters();
                            break;
                        case XmlFormatter.XmlSerializer:
                            mvc.AddXmlSerializerFormatters();
                            break;
                        default:
                            break;
                    }
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }));
        return builder.Start();
    }

    private static async Task<HttpResponseMessage> GetAsync(IHost host, string path, string? accept)
    {
        using var client = host.GetTestClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (accept is not null)
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return await client.SendAsync(req, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(XmlFormatter.DataContract)]
    [InlineData(XmlFormatter.XmlSerializer)]
    [InlineData(XmlFormatter.None)]
    public async Task Failure_is_problem_json_even_when_client_demands_xml(XmlFormatter xml)
    {
        using var host = CreateHost(xml);

        var resp = await GetAsync(host, "/diag/404", "application/xml");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("kind").GetString().Should().Be("not-found");
    }

    /// <summary>
    /// The DataContract formatter is the one that used to throw rather than decline. Its
    /// <c>ProblemDetails.Extensions</c> is typed <c>object?</c> and Trellis fills it with
    /// <c>FieldViolationProblemDetail</c> values, which <c>DataContractSerializer</c> refuses because
    /// it was never given them via <c>KnownTypeAttribute</c> — and no application can supply one.
    /// A validation failure carries the richest extension payload, so it is the sharpest case.
    /// </summary>
    [Fact]
    public async Task Validation_failure_survives_the_datacontract_formatter_with_its_errors_intact()
    {
        using var host = CreateHost(XmlFormatter.DataContract);

        var resp = await GetAsync(host, "/diag/422-fields", "application/xml");

        resp.StatusCode.Should().Be((HttpStatusCode)422);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);

        doc.RootElement.GetProperty("errors").GetProperty("email").EnumerateArray().Should().HaveCount(2);
        doc.RootElement.GetProperty("fieldViolations").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Rule_violations_survive_the_datacontract_formatter()
    {
        using var host = CreateHost(XmlFormatter.DataContract);

        var resp = await GetAsync(host, "/diag/422-rules", "application/xml");

        resp.StatusCode.Should().Be((HttpStatusCode)422);
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("ruleViolations").GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// Bypassing <c>IProblemDetailsService</c> must not cost the customization pipeline: this is the
    /// regression the naive form of the fix would have introduced silently, since every existing
    /// <c>traceId</c> test covers the exception handler or status-code pages rather than the failure
    /// writer.
    /// </summary>
    [Fact]
    public async Task TraceId_from_AddTrellisProblemDetails_still_reaches_failure_writer_output()
    {
        using var host = CreateHost(trellisProblemDetails: true);

        var resp = await GetAsync(host, "/diag/404", null);

        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Consumer_CustomizeProblemDetails_still_runs_over_failure_writer_output()
    {
        using var host = CreateHost(customize: ctx => ctx.ProblemDetails.Extensions["fromConsumer"] = "yes");

        var resp = await GetAsync(host, "/diag/404", null);

        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("fromConsumer").GetString().Should().Be("yes");
    }

    /// <summary>
    /// The customization runs exactly once. Writing the body directly while the framework also wrote
    /// it would double-apply an appending customization, which a counter makes visible where an
    /// assigning customization would not.
    /// </summary>
    [Fact]
    public async Task Customization_runs_exactly_once_per_failure()
    {
        var calls = 0;
        using var host = CreateHost(customize: _ => Interlocked.Increment(ref calls));

        var resp = await GetAsync(host, "/diag/404", null);
        _ = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        calls.Should().Be(1);
    }

    [Fact]
    public async Task Consumer_customization_can_still_override_a_Trellis_default()
    {
        using var host = CreateHost(
            trellisProblemDetails: true,
            customize: ctx => ctx.ProblemDetails.Extensions["traceId"] = "consumer-wins");

        var resp = await GetAsync(host, "/diag/404", null);

        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("traceId").GetString().Should().Be("consumer-wins");
    }

    /// <summary>
    /// The scalar-validation seam is the second door into the same crash, and closing only the
    /// first would have left the vulnerability fully exploitable. `ScalarValueValidationFilter`
    /// answers before the handler runs and never touches `ResponseFailureWriter`; it produced a
    /// `ProblemDetailsActionResult`, which executed an inner `ObjectResult` through MVC's formatter
    /// pipeline. MVC selected the XML formatter for an XML `Accept` even though that inner result
    /// declared `application/problem+json` as its only content type — so pinning the declared list
    /// was not a defence, and the result has to write its own body.
    /// </summary>
    [Theory]
    [InlineData("application/xml")]
    [InlineData("application/problem+xml")]
    [InlineData("*/*")]
    public async Task Scalar_validation_failure_is_problem_json_whatever_the_client_accepts(string accept)
    {
        using var host = CreateScalarValidationHost();
        using var client = host.GetTestClient();

        using var content = new StringContent(
            """{"email":"not-an-email"}""", System.Text.Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/maybe-dto") { Content = content };
        req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(accept));

        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be((HttpStatusCode)422);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);

        // The structured extension values are exactly what DataContractSerializer choked on, so a
        // response that kept them is a response that took the JSON path.
        doc.RootElement.GetProperty("fieldViolations").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("errors").GetProperty("email").EnumerateArray().Should().HaveCount(1);

        // Writing the body directly must not cost what MVC's ProblemDetailsFactory contributed.
        doc.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("kind").GetString().Should().Be("unprocessable-content");
    }

    /// <summary>
    /// The Minimal API seam was measured to be unaffected — it resolves
    /// `IProblemDetailsService` to the non-MVC writer, which never negotiates. Pinned so that a
    /// future change routing it through MVC would be caught rather than silently reopening the
    /// hole on a path nothing else covers.
    /// </summary>
    [Fact]
    public async Task Minimal_api_scalar_validation_failure_is_problem_json_when_client_demands_xml()
    {
        using var host = CreateMinimalApiHost();
        using var client = host.GetTestClient();

        using var content = new StringContent(
            """{"email":"not-an-email"}""", System.Text.Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/mini") { Content = content };
        req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/xml"));

        var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be((HttpStatusCode)422);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await resp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("fieldViolations").GetArrayLength().Should().Be(1);
    }

    private static IHost CreateScalarValidationHost() =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddProblemDetails();
                    s.AddTrellisAspWithScalarValidation();
                    s.AddControllers()
                        .AddApplicationPart(typeof(MaybeDtoController).Assembly)
                        .AddXmlDataContractSerializerFormatters();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseScalarValueValidation();
                    app.UseEndpoints(e => e.MapControllers());
                }))
            .Start();

    private static IHost CreateMinimalApiHost() =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web => web
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddProblemDetails();
                    s.AddTrellisAspWithScalarValidation();
                    s.AddControllers().AddXmlDataContractSerializerFormatters();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseScalarValueValidation();
                    app.UseEndpoints(e => e
                        .MapPost("/mini", (MaybeDtoRequest req) => Results.Ok(req.Email.Value))
                        .WithScalarValueValidation());
                }))
            .Start();
}
