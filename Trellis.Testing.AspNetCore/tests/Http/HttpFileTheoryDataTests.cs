namespace Trellis.Testing.AspNetCore.Tests.Http;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Trellis.Testing.AspNetCore.Http;

/// <summary>
/// Coverage for <see cref="HttpFileTheoryData.FromFile"/>: produces one <c>object[]</c> row
/// per parsed request and propagates external variables through to the parser.
/// </summary>
public class HttpFileTheoryDataTests
{
    [Fact]
    public void FromFile_returns_one_row_per_parsed_request()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trellis-theorydata-{Guid.NewGuid():N}.http");
        try
        {
            File.WriteAllText(path, "### A\nGET http://x/a\n\n### B\nGET http://x/b\n");

            var rows = HttpFileTheoryData.FromFile(path).ToList();

            rows.Should().HaveCount(2);
            rows[0].Should().ContainSingle().Which.Should().BeOfType<HttpFileRequest>();
            ((HttpFileRequest)rows[0][0]).Title.Should().Be("A");
            ((HttpFileRequest)rows[1][0]).Title.Should().Be("B");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FromFile_forwards_external_variables_to_parser()
    {
        var path = Path.Combine(Path.GetTempPath(), $"trellis-theorydata-{Guid.NewGuid():N}.http");
        try
        {
            File.WriteAllText(path, "### X\nGET {{host}}/x\n");
            var vars = new Dictionary<string, string> { ["host"] = "http://ext" };

            var rows = HttpFileTheoryData.FromFile(path, vars).ToList();

            rows.Should().HaveCount(1);
            ((HttpFileRequest)rows[0][0]).Url.Should().Be("http://ext/x");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}