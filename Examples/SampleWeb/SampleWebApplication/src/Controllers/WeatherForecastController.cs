using Trellis.Asp;

namespace SampleWebApplication.Controllers;

using System.Collections.Immutable;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using Trellis;
using static Trellis.Error.UnprocessableContent;
using System.Globalization;

[ApiController]
[Produces("application/json")]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    private static readonly string[] s_summaries =
    [
        "Freezing",
        "Bracing",
        "Chilly",
        "Cool",
        "Mild",
        "Warm",
        "Balmy",
        "Hot",
        "Sweltering",
        "Scorching"
    ];

    [HttpGet(Name = "GetWeatherForecast")]
    public ActionResult<WeatherForecast[]> Get()
    {
        long from = 0;
        long to = 4;
        var strRange = Request.Headers[Microsoft.Net.Http.Headers.HeaderNames.Range].FirstOrDefault();
        if (RangeHeaderValue.TryParse(strRange, out var range))
        {
            var firstRange = range.Ranges.First();
            from = firstRange.From ?? from;
            to = firstRange.To ?? to;
        }

        var data = Enumerable.Range(1, 5).Select(index => new WeatherForecast
        {
            Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = s_summaries[Random.Shared.Next(s_summaries.Length)]
        }).ToArray();

        WeatherForecast[] page = data.Skip((int)from).Take((int)(to - from + 1)).ToArray();
        var contentRangeHeaderValue = new ContentRangeHeaderValue(from, to, data.Length) { Unit = "items" };

        return Result.Ok<(ContentRangeHeaderValue, WeatherForecast[])>((contentRangeHeaderValue, page))
            .ToActionResult(this, static r => r.Item1, static r => r.Item2);
    }

    [HttpGet("Forbidden")]
    public ActionResult Forbidden(string instance)
        => new Error.Forbidden(instance) { Detail = "You are forbidden." }.ToActionResult(this);

    [HttpGet("Unauthorized")]
    public ActionResult Unauthorized(string instance)
        => new Error.Unauthorized() { Detail = "You are not authorized." }.ToActionResult(this);

    [HttpGet("Conflict")]
    public ActionResult Conflict(string instance)
        => new Error.Conflict(null, instance) { Detail = "There is a conflict. " + instance }.ToActionResult(this);

    [HttpGet("NotFound")]
    public ActionResult NotFound(string? instance)
        => new Error.NotFound(new ResourceRef("Resource", instance)) { Detail = "Record not found. " + instance }.ToActionResult(this);

    [HttpGet("ValidationError")]
    public ActionResult ValidationError(string? instance, string? detail)
    {
        Error error = new Error.UnprocessableContent(EquatableArray.Create(
            new FieldViolation(InputPointer.ForProperty("Field1"), "validation.error") { Detail = "Field is required." },
            new FieldViolation(InputPointer.ForProperty("Field1"), "validation.error") { Detail = "It cannot be empty." },
            new FieldViolation(InputPointer.ForProperty("Field2"), "validation.error") { Detail = "Field is required." }))
            { Detail = detail };
        return error.ToActionResult(this);
    }
}