using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>Public holidays. Not employee data, but still needs a signed-in user like every controller.</summary>
[ApiController, Route("holidays")]
public class HolidaysController : ControllerBase
{
    /// <summary>GET /holidays?state=SEL&amp;year=2026. State is ALL (national only), SEL, KUL or PNG.</summary>
    [HttpGet]
    public IActionResult Get([FromQuery] string? state, [FromQuery] int? year)
    {
        try { return Ok(HolidayService.For(state, year)); }
        catch (HolidayException e) { return Problem(statusCode: 400, title: "Holiday query not valid", detail: e.Message); }
    }
}
