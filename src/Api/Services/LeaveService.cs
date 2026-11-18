using Api.Data;
using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public record LeaveBalance(int EmployeeId, decimal Entitlement, decimal Used, decimal Pending, decimal Available);

/// <summary>Body of POST /leave. No EmployeeId on purpose: the employee is always the signed-in user.</summary>
public record CreateLeave(DateOnly? StartDate, DateOnly? EndDate, HalfDay HalfDay, string? Reason);

/// <summary>A leave request as the API returns it, with its working days.</summary>
public record LeaveView(int Id, int EmployeeId, DateOnly StartDate, DateOnly EndDate, HalfDay HalfDay, LeaveStatus Status, string Reason, decimal Days);

/// <summary>Either a value or a broken-rule message (400).</summary>
public record LeaveResult(LeaveView? Value, string? Error)
{
    public static LeaveResult Ok(LeaveView v) => new(v, null);
    public static LeaveResult Fail(string error) => new(null, error);
}

public class LeaveService(AcmeDb db)
{
    public const int MaxReasonLength = 500;
    public const decimal AutoApproveMaxDays = 3;

    public static LeaveView ToView(LeaveRequest r) =>
        new(r.Id, r.EmployeeId, r.StartDate, r.EndDate, r.HalfDay, r.Status, r.Reason, DaysOf(r));

    public static decimal DaysOf(LeaveRequest r) =>
        r.HalfDay == HalfDay.None ? WorkingDays.Count(r.StartDate, r.EndDate) : 0.5m;

    public async Task<LeaveBalance?> GetBalanceAsync(int employeeId)
    {
        var emp = await db.Employees.FindAsync(employeeId);
        if (emp is null) return null;
        var requests = await db.LeaveRequests.Where(r => r.EmployeeId == employeeId).ToListAsync();
        var used = requests.Where(r => r.Status == LeaveStatus.Approved).Sum(DaysOf);
        var pending = requests.Where(r => r.Status == LeaveStatus.Pending).Sum(DaysOf);
        return new LeaveBalance(employeeId, emp.AnnualEntitlement, used, pending, emp.AnnualEntitlement - used - pending);
    }

    public Task<List<LeaveRequest>> ListAsync(int? employeeId) =>
        db.LeaveRequests.Where(r => employeeId == null || r.EmployeeId == employeeId)
            .OrderByDescending(r => r.StartDate).ToListAsync();

    public Task<LeaveRequest?> FindAsync(int id) => db.LeaveRequests.FirstOrDefaultAsync(r => r.Id == id);

    /// <summary>Create leave for the signed-in employee (docs/feature-spec-leave.md, rules 1-5, 9-11).</summary>
    public async Task<LeaveResult> CreateAsync(int employeeId, CreateLeave input)
    {
        if (input.StartDate is not { } start) return LeaveResult.Fail("startDate is required.");
        if (input.EndDate is not { } end) return LeaveResult.Fail("endDate is required.");
        if (end < start) return LeaveResult.Fail("endDate must not be before startDate.");
        if (input.HalfDay != HalfDay.None)
        {
            if (start != end) return LeaveResult.Fail("A half day must start and end on the same date.");
            if (WorkingDays.Count(start, end) == 0) return LeaveResult.Fail("A half day must be on a working day (Mon-Fri).");
        }
        var reason = input.Reason?.Trim() ?? "";
        if (reason.Length == 0) return LeaveResult.Fail("A reason is required.");
        if (reason.Length > MaxReasonLength) return LeaveResult.Fail($"The reason must be at most {MaxReasonLength} characters.");

        var request = new LeaveRequest { EmployeeId = employeeId, StartDate = start, EndDate = end, HalfDay = input.HalfDay, Reason = reason };
        var days = DaysOf(request);
        if (days == 0) return LeaveResult.Fail("The request has no working days (Mon-Fri).");

        var active = await db.LeaveRequests
            .Where(r => r.EmployeeId == employeeId && r.Status != LeaveStatus.Rejected && r.StartDate <= end && r.EndDate >= start)
            .ToListAsync();
        if (active.Any(r => Overlaps(r, request)))
            return LeaveResult.Fail("The request overlaps another pending or approved leave request.");

        if (await GetBalanceAsync(employeeId) is not { } balance) return LeaveResult.Fail($"Employee {employeeId} not found.");
        if (balance.Available < days)
            return LeaveResult.Fail($"Not enough leave balance: {days} days requested, {balance.Available} available.");

        request.Status = days <= AutoApproveMaxDays ? LeaveStatus.Approved : LeaveStatus.Pending;
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();
        return LeaveResult.Ok(ToView(request));
    }

    /// <summary>Approve or reject. The caller has already checked who may decide (AccessService.CanDecideLeaveAsync).</summary>
    public async Task<LeaveResult> DecideAsync(LeaveRequest request, bool approve)
    {
        if (request.Status != LeaveStatus.Pending)
            return LeaveResult.Fail($"Only pending requests can be approved or rejected. This one is {request.Status}.");
        request.Status = approve ? LeaveStatus.Approved : LeaveStatus.Rejected;
        await db.SaveChangesAsync();
        return LeaveResult.Ok(ToView(request));
    }

    /// <summary>Date ranges already intersect. Only a morning and an afternoon of the same single date may coexist.</summary>
    static bool Overlaps(LeaveRequest a, LeaveRequest b) =>
        !(a.HalfDay != HalfDay.None && b.HalfDay != HalfDay.None && a.HalfDay != b.HalfDay);
}
