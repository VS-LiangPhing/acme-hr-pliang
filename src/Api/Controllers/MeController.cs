using Api.Auth;
using Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>The signed-in employee, their manager's name and their roles. Sign in first with POST /auth/login.</summary>
[ApiController, Route("me")]
public class MeController(ProfileService profiles) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var p = await profiles.GetAsync(User.EmployeeId());
        if (p is null) return Problem(statusCode: 404, title: "No employee record for this sign-in");
        var roles = Roles.All.Where(User.IsInRole).ToArray(); // Employee, Manager, HR order
        return Ok(new { p.Id, p.Name, p.Email, p.Department, p.ManagerId, p.ManagerName, p.AnnualEntitlement, Roles = roles });
    }
}
