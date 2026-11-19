using System.ComponentModel.DataAnnotations;
using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>Public holidays per state. Not employee data: any signed-in user may call it.</summary>
[ApiController, Route("holidays")]
public class HolidaysController(HolidayService holidays) : ControllerBase
{
    /// <summary>Holidays in a state and year that fall on a Saturday or Sunday, with the POL-13 replacement day for Sundays.</summary>
    /// <param name="state">SEL, KUL or PNG.</param>
    /// <param name="year">Defaults to the current year.</param>
    [HttpGet("weekends")]
    public IActionResult Weekends([FromQuery, Required] string? state, [FromQuery] int? year)
    {
        try { return Ok(holidays.OnWeekends(state!, year ?? DateTime.UtcNow.Year)); }
        catch (HolidayException e) { return Problem(statusCode: 400, title: "Holiday query not valid", detail: e.Message); }
    }
}
