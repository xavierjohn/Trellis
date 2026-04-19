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

        return Result.Success<(ContentRangeHeaderValue, WeatherForecast[])>(() =>
            {
                var allData = Enumerable.Range(1, 5).Select(index => new WeatherForecast
                {
                    Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    TemperatureC = Random.Shared.Next(-20, 55),
                    Summary = s_summaries[Random.Shared.Next(s_summaries.Length)]
                }).ToArray();

                WeatherForecast[] data = allData.Skip((int)from).Take((int)(to - from + 1)).ToArray();
                var contentRangeHeaderValue = new ContentRangeHeaderValue(from, to, allData.Length) { Unit = "items" };
                return new(contentRangeHeaderValue, data);
            })
        .ToActionResult(this, static r => r.Item1, static r => r.Item2);
    }

    [HttpGet("Forbidden")]
    public ActionResult<Unit> Forbidden(string instance)
        => Error.Forbidden("You are forbidden.", instance).ToActionResult<Unit>(this);

    [HttpGet("Unauthorized")]
    public ActionResult<Unit> Unauthorized(string instance)
        => Error.Unauthorized("You are not authorized.", instance).ToActionResult<Unit>(this);

    [HttpGet("Conflict")]
    public ActionResult<Unit> Conflict(string instance)
        => Error.Conflict("There is a conflict. " + instance, instance).ToActionResult<Unit>(this);

    [HttpGet("NotFound")]
    public ActionResult<Unit> NotFound(string? instance)
        => Error.NotFound("Record not found. " + instance, instance).ToActionResult<Unit>(this);

    [HttpGet("ValidationError")]
    public ActionResult<Unit> ValidationError(string? instance, string? detail)
    {
        ImmutableArray<FieldError> errors = [
            new("Field1",["Field is required.", "It cannot be empty."]),
            new("Field2",["Field is required."])
        ];
        return Error.Validation(errors, detail ?? string.Empty, instance).ToActionResult<Unit>(this);
    }
}