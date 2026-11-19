using Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public record Profile(int Id, string Name, string Email, string Department, int? ManagerId, string? ManagerName, decimal AnnualEntitlement);

/// <summary>An employee's own profile. Callers pass the signed-in employee's id, never one from the request.</summary>
public class ProfileService(AcmeDb db)
{
    public async Task<Profile?> GetAsync(int employeeId)
    {
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employeeId);
        if (e is null) return null;
        var managerName = e.ManagerId is int m
            ? await db.Employees.Where(x => x.Id == m).Select(x => x.Name).FirstOrDefaultAsync()
            : null;
        return new Profile(e.Id, e.Name, e.Email, e.Department, e.ManagerId, managerName, e.AnnualEntitlement);
    }
}
