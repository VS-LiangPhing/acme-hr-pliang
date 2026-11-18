using Api.Auth;
using Api.Data;
using Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

[ApiController, Route("health"), AllowAnonymous]
public class HealthController(AcmeDb db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var ok = await db.Database.CanConnectAsync();
        return Ok(new { status = ok ? "ok" : "error", database = ok ? "database connected" : "database NOT connected", provider = db.Database.ProviderName });
    }
}

[ApiController, Route("employees")]
public class EmployeesController(AcmeDb db, LeaveService leave, AccessService access) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await db.Employees.OrderBy(e => e.Id).ToListAsync());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) =>
        await db.Employees.FindAsync(id) is { } e ? Ok(e) : Problem(statusCode: 404, title: $"Employee {id} not found");

    [HttpGet("{id:int}/balance")]
    public async Task<IActionResult> Balance(int id)
    {
        if (!await access.CanSeeEmployeeAsync(User, id)) return Problem(statusCode: 403, title: "You can only see your own or your team's leave");
        return await leave.GetBalanceAsync(id) is { } b ? Ok(b) : Problem(statusCode: 404, title: $"Employee {id} not found");
    }
}

[ApiController, Route("leave")]
public class LeaveController(LeaveService leave, AccessService access) : ControllerBase
{
    /// <summary>Leave you may see: your own, your direct reports' (managers), everyone's (HR).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? employeeId)
    {
        if (employeeId is int id)
            return await access.CanSeeEmployeeAsync(User, id)
                ? Ok((await leave.ListAsync(id)).Select(LeaveService.ToView))
                : Problem(statusCode: 403, title: "You can only see your own or your team's leave");
        var visible = await access.VisibleEmployeeIdsAsync(User);
        return Ok((await leave.ListAsync(null)).Where(r => visible is null || visible.Contains(r.EmployeeId)).Select(LeaveService.ToView));
    }

    /// <summary>Request leave for yourself. Up to 3 working days is approved at once; more waits for your manager.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateLeave body)
    {
        var res = await leave.CreateAsync(User.EmployeeId(), body);
        return res.Error is { } error ? Problem(statusCode: 400, title: error) : Created($"/leave/{res.Value!.Id}", res.Value);
    }

    [HttpPost("{id:int}/approve")]
    public Task<IActionResult> Approve(int id) => Decide(id, approve: true);

    [HttpPost("{id:int}/reject")]
    public Task<IActionResult> Reject(int id) => Decide(id, approve: false);

    // Order: unknown → 404, not allowed → 403, then rule checks → 400. Outsiders learn nothing about the request.
    async Task<IActionResult> Decide(int id, bool approve)
    {
        if (await leave.FindAsync(id) is not { } request) return Problem(statusCode: 404, title: $"Leave request {id} not found");
        if (!await access.CanDecideLeaveAsync(User, request))
            return Problem(statusCode: 403, title: "Only the requester's manager or HR can decide, and never on their own request");
        var res = await leave.DecideAsync(request, approve);
        return res.Error is { } error ? Problem(statusCode: 400, title: error) : Ok(res.Value);
    }
}

[ApiController, Route("policies")]
public class PoliciesController(AcmeDb db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await db.Policies.OrderBy(p => p.Id).Select(p => new { p.Id, p.Title }).ToListAsync());
}
