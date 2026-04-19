using Trellis.Asp;

namespace SampleWebApplication.Controllers;

using System.Collections.Immutable;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using Trellis;
using static Trellis.ValidationError;

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
        => Error.Forbidden("You are forbidden.", instance).ToActionResult(this);

    [HttpGet("Unauthorized")]
    public ActionResult Unauthorized(string instance)
        => Error.Unauthorized("You are not authorized.", instance).ToActionResult(this);

    [HttpGet("Conflict")]
    public ActionResult Conflict(string instance)
        => Error.Conflict("There is a conflict. " + instance, instance).ToActionResult(this);

    [HttpGet("NotFound")]
    public ActionResult NotFound(string? instance)
        => Error.NotFound("Record not found. " + instance, instance).ToActionResult(this);

    [HttpGet("ValidationError")]
    public ActionResult ValidationError(string? instance, string? detail)
    {
        ImmutableArray<FieldError> errors = [
            new("Field1",["Field is required.", "It cannot be empty."]),
            new("Field2",["Field is required."])
        ];
        return Error.Validation(errors, detail ?? string.Empty, instance).ToActionResult(this);
    }
}