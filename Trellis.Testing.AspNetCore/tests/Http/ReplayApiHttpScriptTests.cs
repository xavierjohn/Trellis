namespace Trellis.Testing.AspNetCore.Tests.Http;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Trellis.Testing.AspNetCore.Http;

public class ReplayApiHttpScriptTests
{
    [Fact]
    public async Task Replay_GuidOccurrences_RepeatedExecution_GeneratesDistinctValues()
    {
        const string file = """
            ### Create
            POST {{host}}/things/{{$guid}}
            Idempotency-Key: {{$guid}}
            X-Second: {{guidAlias}}
            Content-Type: application/json

            {"first":"{{$guid}}","second":"{{$guid}}"}

            ### Next
            GET {{host}}/next/{{$guid}}
            """;

        using var report = await ReplayAsync(file, 2);

        report.RootElement.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        report.RootElement.GetProperty("transcript").GetString().Should().NotContain("{{");
        var sent = report.RootElement.GetProperty("sent");
        sent.GetArrayLength().Should().Be(4);
        var values = new List<string>();
        foreach (var request in sent.EnumerateArray())
        {
            values.Add(new Uri(request.GetProperty("url").GetString()!).Segments[^1]);
            var bodyText = request.GetProperty("body").GetString();
            if (string.IsNullOrEmpty(bodyText))
                continue;
            var headers = request.GetProperty("headers");
            values.Add(headers.GetProperty("Idempotency-Key").GetString()!);
            values.Add(headers.GetProperty("X-Second").GetString()!);
            using var body = JsonDocument.Parse(bodyText);
            values.Add(body.RootElement.GetProperty("first").GetString()!);
            values.Add(body.RootElement.GetProperty("second").GetString()!);
        }

        values.Should().HaveCount(12).And.OnlyHaveUniqueItems();
        foreach (var value in values)
            Guid.TryParseExact(value, "D", out _).Should().BeTrue("{0} must be an expanded GUID", value);
    }

    [Theory]
    [InlineData("URL", "{{missing}}")]
    [InlineData("header", "{{missing}}")]
    [InlineData("content header", "{{missing}}")]
    [InlineData("header name", "{{missing}}")]
    [InlineData("header name", "{{ missing }}")]
    [InlineData("body", "{{missing}}")]
    [InlineData("URL", "{{create.response.body.id}}")]
    [InlineData("header", "{{$timestamp}}")]
    [InlineData("body", "{{$randomInt}}")]
    [InlineData("URL", "{{$datetime}}")]
    [InlineData("body", "{{unclosed")]
    public async Task Replay_UnresolvedPlaceholder_InRequestFields_RejectsBeforeSending(string location, string token)
    {
        var url = location == "URL" ? "{{host}}/things/" + token : "{{host}}/things";
        var header = location == "header" ? token : "known";
        var headerName = location == "header name" ? token : "X-Value";
        var contentType = location == "content header" ? token : "text/plain";
        var body = location == "body" ? "private-body " + token : "private-body";
        var file = $"### Reject token\nPOST {url}\n{headerName}: {header}\nContent-Type: {contentType}\n\n{body}\n\n### Next\nGET {{{{host}}}}/next\n";

        using var report = await ReplayAsync(file);

        report.RootElement.GetProperty("sent").GetArrayLength().Should().Be(0);
        var error = report.RootElement.GetProperty("error").GetString();
        error.Should().Contain("Reject token").And.Contain("{{").And.NotContain("private-body");
        error.Should().Contain(location == "content header" ? "header 'Content-Type'" : location);
        if (token.EndsWith("}}", StringComparison.Ordinal))
            error.Should().Contain(token);
    }

    [Fact]
    public async Task Replay_UnresolvedPlaceholder_InLaterRequest_StopsTheSequence()
    {
        using var report = await ReplayAsync("""
            ### First
            GET {{host}}/first

            ### Reject token
            GET {{host}}/{{missing}}

            ### Never sent
            GET {{host}}/third
            """);

        report.RootElement.GetProperty("sent").GetArrayLength().Should().Be(1);
        report.RootElement.GetProperty("error").GetString().Should().Contain("Reject token").And.Contain("{{missing}}");
    }

    [Fact]
    public async Task Replay_SingleRequest_WithStaticVariables_ProducesTranscript()
    {
        using var report = await ReplayAsync("### Single\nGET {{host}}/things\n");

        report.RootElement.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
        report.RootElement.GetProperty("sent").GetArrayLength().Should().Be(1);
        report.RootElement.GetProperty("transcript").GetString().Should().Contain("Single").And.Contain("/things");
    }

    [Fact]
    public async Task Replay_EmptyFile_WithoutRequests_ReportsNoRequests()
    {
        using var report = await ReplayAsync("# No requests\n");

        report.RootElement.GetProperty("sent").GetArrayLength().Should().Be(0);
        report.RootElement.GetProperty("error").GetString().Should().Contain("No requests parsed");
    }

    [Fact]
    public async Task Replay_BothPaths_WithRealLoopbackHost_ExpandsGuidsAndRejectsMissingVariables()
    {
        var received = new ConcurrentQueue<(string Id, string Key, string Body)>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.MapPost("/things/{id}", async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            received.Enqueue((
                context.Request.RouteValues["id"]!.ToString()!,
                context.Request.Headers["Idempotency-Key"].ToString(),
                await reader.ReadToEndAsync(context.RequestAborted)));
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{}", context.RequestAborted);
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            var host = app.Urls.Single();
            const string file = """
                ### Create
                POST {{host}}/things/{{$guid}}
                Idempotency-Key: {{$guid}}
                Content-Type: application/json

                {"guid":"{{$guid}}"}
                """;
            using var client = new HttpClient();
            var requests = HttpFileParser.Parse(file, new Dictionary<string, string> { ["host"] = host });
            for (var replay = 0; replay < 2; replay++)
            {
                var results = await HttpFileRunner.RunAsync(client, requests, TestContext.Current.CancellationToken);
                foreach (var result in results)
                {
                    using var response = result.Response;
                    HttpFileAssertions.AssertExpectationsMet(result);
                }
            }

            using var scriptReport = await ReplayAsync(file, 2, host, mockSend: false);
            scriptReport.RootElement.GetProperty("error").ValueKind.Should().Be(JsonValueKind.Null);
            received.Should().HaveCount(4);
            var values = new List<string>();
            foreach (var request in received)
            {
                values.Add(request.Id);
                values.Add(request.Key);
                using var body = JsonDocument.Parse(request.Body);
                values.Add(body.RootElement.GetProperty("guid").GetString()!);
            }

            values.Should().HaveCount(12).And.OnlyHaveUniqueItems();
            foreach (var value in values)
                Guid.TryParseExact(value, "D", out _).Should().BeTrue("{0} must be an expanded GUID", value);

            const string invalidFile = "### Reject token\nPOST {{host}}/things/invalid\nX-Value: {{missing}}\n";
            var invalidRequests = HttpFileParser.Parse(invalidFile, new Dictionary<string, string> { ["host"] = host });
            Func<Task> act = async () => await HttpFileRunner.RunAsync(client, invalidRequests, TestContext.Current.CancellationToken);
            await act.Should().ThrowAsync<HttpFileAssertionException>().WithMessage("*{{missing}}*");
            using var invalidReport = await ReplayAsync(invalidFile, 1, host, mockSend: false);
            invalidReport.RootElement.GetProperty("error").GetString().Should().Contain("{{missing}}");
            received.Should().HaveCount(4);
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<JsonDocument> ReplayAsync(
        string content, int repetitions = 1, string host = "http://fake", bool mockSend = true)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Trellis.slnx")))
            root = root.Parent;
        if (root is null)
            throw new DirectoryNotFoundException("Could not find Trellis.slnx for the Showcase replay script tests.");

        var directory = Path.Combine(Path.GetTempPath(), $"trellis-http-replay-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var script = Path.Combine(directory, "replay-api-http.ps1");
            var output = Path.Combine(directory, "captured.json");
            var transcript = Path.Combine(directory, "transcript.txt");
            File.Copy(Path.Combine(root.FullName, "Examples", "Showcase", "replay-api-http.ps1"), script);
            await File.WriteAllTextAsync(Path.Combine(directory, "api.http"), content, new UTF8Encoding(true), TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "http-client.env.json"),
                JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>>
                {
                    ["$shared"] = new() { ["$guid"] = "not-a-dynamic-guid", ["guidAlias"] = "{{$guid}}" },
                    ["mvc"] = new() { ["host"] = host },
                }),
                new UTF8Encoding(true), TestContext.Current.CancellationToken);
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                $global:sent = [System.Collections.Generic.List[object]]::new()
                if ({{(mockSend ? "$true" : "$false")}}) {
                    function Invoke-WebRequest {
                        [CmdletBinding()]
                        param(
                            [uri]$Uri, [string]$Method, [hashtable]$Headers,
                            [string]$Body, [string]$ContentType,
                            [switch]$SkipCertificateCheck, [switch]$SkipHttpErrorCheck,
                            [int]$MaximumRedirection
                        )
                        $global:sent.Add([pscustomobject]@{
                            url = $Uri.OriginalString
                            headers = $Headers
                            body = $Body
                        })
                        [pscustomobject]@{
                            StatusCode = 200
                            Headers = @{ 'Content-Type' = @('application/json') }
                            Content = '{}'
                        }
                    }
                }
                $errorMessage = $null
                try {
                    for ($i = 0; $i -lt {{repetitions}}; $i++) {
                        & '{{script.Replace("'", "''")}}' -Environment mvc -TranscriptPath '{{transcript.Replace("'", "''")}}'
                    }
                }
                catch {
                    $errorMessage = $_.Exception.Message
                }
                $transcriptText = if (Test-Path -LiteralPath '{{transcript.Replace("'", "''")}}') {
                    [System.IO.File]::ReadAllText('{{transcript.Replace("'", "''")}}')
                } else { $null }
                $report = @{ sent = @($global:sent.ToArray()); error = $errorMessage; transcript = $transcriptText } | ConvertTo-Json -Depth 20
                [System.IO.File]::WriteAllText('{{output.Replace("'", "''")}}', $report, [System.Text.UTF8Encoding]::new($true))
                """;
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("pwsh")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.StartInfo.ArgumentList.Add("-NoProfile");
            process.StartInfo.ArgumentList.Add("-NonInteractive");
            process.StartInfo.ArgumentList.Add("-EncodedCommand");
            process.StartInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
            process.Start().Should().BeTrue();
            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                await process.WaitForExitAsync(timeout.Token);
                process.ExitCode.Should().Be(0, "{0}\n{1}", await stdout, await stderr);
                return JsonDocument.Parse(await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
