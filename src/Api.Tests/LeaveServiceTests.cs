using Api.Models;
using Api.Services;

namespace Api.Tests;

public class LeaveServiceTests
{
    [Fact]
    public async Task Balance_for_new_employee_is_full_entitlement()
    {
        using var db = TestDb.Create();
        var b = await new LeaveService(db).GetBalanceAsync(1);
        Assert.NotNull(b);
        Assert.Equal(b!.Entitlement, b.Available);
    }

    [Fact]
    public async Task Balance_for_unknown_employee_is_null()
    {
        using var db = TestDb.Create();
        Assert.Null(await new LeaveService(db).GetBalanceAsync(999));
    }

    [Fact]
    public async Task Approved_leave_reduces_balance()
    {
        using var db = TestDb.Create();
        var b = await new LeaveService(db).GetBalanceAsync(6); // seed: 3 approved days
        Assert.Equal(3, b!.Used);
    }

    [Fact]
    public async Task Half_day_counts_half()
    {
        using var db = TestDb.Create();
        var b = await new LeaveService(db).GetBalanceAsync(7); // seed: AM half day
        Assert.Equal(0.5m, b!.Used);
    }

    [Fact]
    public async Task Pending_leave_is_held_from_available()
    {
        using var db = TestDb.Create();
        var b = await new LeaveService(db).GetBalanceAsync(8); // seed: 5 pending days
        Assert.Equal(5, b!.Pending);
        Assert.Equal(b.Entitlement - 5, b.Available);
    }

    [Fact]
    public async Task List_filters_by_employee()
    {
        using var db = TestDb.Create();
        var list = await new LeaveService(db).ListAsync(6);
        Assert.All(list, r => Assert.Equal(6, r.EmployeeId));
    }

    [Fact]
    public void Rejected_leave_days_still_computed() =>
        Assert.Equal(1, LeaveService.DaysOf(new LeaveRequest { StartDate = new(2026, 11, 18), EndDate = new(2026, 11, 18), Status = LeaveStatus.Rejected }));
}

// Creating and deciding leave (docs/feature-spec-leave.md). Farah (6) has 18 days, 3 approved in March: 15 available.
// Dates: Mon 2026-12-07 ... Fri 2026-12-11, Sat 12, Sun 13, Mon 14.
public class LeaveCreateTests
{
    const int Farah = 6, Gopal = 7;

    static CreateLeave Req(string start, string end, HalfDay half = HalfDay.None, string? reason = "Trip") =>
        new(DateOnly.Parse(start), DateOnly.Parse(end), half, reason);

    static async Task<LeaveView> Ok(LeaveService s, int emp, CreateLeave r)
    {
        var res = await s.CreateAsync(emp, r);
        Assert.Null(res.Error);
        return res.Value!;
    }

    static async Task Fails(LeaveService s, int emp, CreateLeave r, string contains)
    {
        var res = await s.CreateAsync(emp, r);
        Assert.Null(res.Value);
        Assert.Contains(contains, res.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Up_to_three_days_is_approved_at_once()
    {
        using var db = TestDb.Create();
        var v = await Ok(new LeaveService(db), Farah, Req("2026-12-07", "2026-12-09"));
        Assert.Equal(LeaveStatus.Approved, v.Status);
        Assert.Equal(3, v.Days);
        Assert.Equal(Farah, v.EmployeeId);
    }

    [Fact]
    public async Task More_than_three_days_waits_for_the_manager()
    {
        using var db = TestDb.Create();
        var v = await Ok(new LeaveService(db), Farah, Req("2026-12-07", "2026-12-10"));
        Assert.Equal(LeaveStatus.Pending, v.Status);
        Assert.Equal(4, v.Days);
    }

    [Fact]
    public async Task Half_day_counts_half_and_is_approved()
    {
        using var db = TestDb.Create();
        var v = await Ok(new LeaveService(db), Farah, Req("2026-12-07", "2026-12-07", HalfDay.AM));
        Assert.Equal(0.5m, v.Days);
        Assert.Equal(LeaveStatus.Approved, v.Status);
    }

    [Fact]
    public async Task Friday_to_Monday_is_two_days()
    {
        using var db = TestDb.Create();
        Assert.Equal(2, (await Ok(new LeaveService(db), Farah, Req("2026-12-11", "2026-12-14"))).Days);
    }

    [Fact]
    public async Task Past_dates_are_allowed()
    {
        using var db = TestDb.Create();
        await Ok(new LeaveService(db), Farah, Req("2026-01-05", "2026-01-05"));
    }

    [Fact]
    public async Task End_before_start_is_rejected()
    {
        using var db = TestDb.Create();
        await Fails(new LeaveService(db), Farah, Req("2026-12-09", "2026-12-07"), "before");
    }

    [Fact]
    public async Task Missing_dates_are_rejected()
    {
        using var db = TestDb.Create();
        var s = new LeaveService(db);
        await Fails(s, Farah, new CreateLeave(null, DateOnly.Parse("2026-12-07"), HalfDay.None, "Trip"), "startDate");
        await Fails(s, Farah, new CreateLeave(DateOnly.Parse("2026-12-07"), null, HalfDay.None, "Trip"), "endDate");
        Assert.Empty(db.LeaveRequests.Where(r => r.StartDate == default || r.EndDate == default));
    }

    [Fact]
    public async Task Half_day_must_be_one_date()
    {
        using var db = TestDb.Create();
        await Fails(new LeaveService(db), Farah, Req("2026-12-07", "2026-12-08", HalfDay.PM), "half day");
    }

    [Fact]
    public async Task Half_day_must_be_a_working_day()
    {
        using var db = TestDb.Create();
        await Fails(new LeaveService(db), Farah, Req("2026-12-12", "2026-12-12", HalfDay.AM), "working day");
    }

    [Fact]
    public async Task Weekend_only_request_is_rejected()
    {
        using var db = TestDb.Create();
        await Fails(new LeaveService(db), Farah, Req("2026-12-12", "2026-12-13"), "working day");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reason_is_required(string? reason)
    {
        using var db = TestDb.Create();
        await Fails(new LeaveService(db), Farah, Req("2026-12-07", "2026-12-07", reason: reason), "reason");
    }

    [Fact]
    public async Task Reason_has_a_length_limit()
    {
        using var db = TestDb.Create();
        await Fails(new LeaveService(db), Farah, Req("2026-12-07", "2026-12-07", reason: new string('x', 501)), "reason");
    }

    [Fact]
    public async Task More_days_than_available_is_rejected()
    {
        using var db = TestDb.Create();
        await Fails(new LeaveService(db), Farah, Req("2026-12-07", "2027-01-01"), "balance"); // 20 days, 15 available
    }

    [Fact]
    public async Task Pending_leave_counts_against_the_balance()
    {
        using var db = TestDb.Create();
        var s = new LeaveService(db);
        await Ok(s, Farah, Req("2026-12-07", "2026-12-18")); // 10 days pending, 5 left
        await Fails(s, Farah, Req("2027-01-04", "2027-01-11"), "balance"); // 6 days
    }

    [Theory]
    [InlineData(LeaveStatus.Approved)]
    [InlineData(LeaveStatus.Pending)]
    public async Task Overlap_with_own_active_leave_is_rejected(LeaveStatus existing)
    {
        using var db = TestDb.Create();
        db.LeaveRequests.Add(new LeaveRequest { EmployeeId = Farah, StartDate = new(2026, 12, 7), EndDate = new(2026, 12, 9), Status = existing, Reason = "x" });
        db.SaveChanges();
        await Fails(new LeaveService(db), Farah, Req("2026-12-09", "2026-12-10"), "overlap");
    }

    [Fact]
    public async Task Rejected_leave_does_not_block_the_dates()
    {
        using var db = TestDb.Create();
        db.LeaveRequests.Add(new LeaveRequest { EmployeeId = Farah, StartDate = new(2026, 12, 7), EndDate = new(2026, 12, 9), Status = LeaveStatus.Rejected, Reason = "x" });
        db.SaveChanges();
        await Ok(new LeaveService(db), Farah, Req("2026-12-08", "2026-12-08"));
    }

    [Fact]
    public async Task Someone_elses_leave_does_not_block_the_dates()
    {
        using var db = TestDb.Create();
        var s = new LeaveService(db);
        await Ok(s, Gopal, Req("2026-12-07", "2026-12-07"));
        await Ok(s, Farah, Req("2026-12-07", "2026-12-07"));
    }

    [Fact]
    public async Task Morning_and_afternoon_of_the_same_day_do_not_overlap()
    {
        using var db = TestDb.Create();
        var s = new LeaveService(db);
        await Ok(s, Farah, Req("2026-12-07", "2026-12-07", HalfDay.AM));
        await Ok(s, Farah, Req("2026-12-07", "2026-12-07", HalfDay.PM));
    }

    [Fact]
    public async Task Same_half_twice_overlaps()
    {
        using var db = TestDb.Create();
        var s = new LeaveService(db);
        await Ok(s, Farah, Req("2026-12-07", "2026-12-07", HalfDay.AM));
        await Fails(s, Farah, Req("2026-12-07", "2026-12-07", HalfDay.AM), "overlap");
    }

    [Fact]
    public async Task Full_day_and_half_day_on_the_same_date_overlap()
    {
        using var db = TestDb.Create();
        var s = new LeaveService(db);
        await Ok(s, Farah, Req("2026-12-07", "2026-12-08"));
        await Fails(s, Farah, Req("2026-12-08", "2026-12-08", HalfDay.PM), "overlap");
    }

    [Theory]
    [InlineData(true, LeaveStatus.Approved)]
    [InlineData(false, LeaveStatus.Rejected)]
    public async Task Pending_request_can_be_decided(bool approve, LeaveStatus expected)
    {
        using var db = TestDb.Create();
        var s = new LeaveService(db);
        var created = await Ok(s, Farah, Req("2026-12-07", "2026-12-10"));
        var res = await s.DecideAsync((await s.FindAsync(created.Id))!, approve);
        Assert.Null(res.Error);
        Assert.Equal(expected, res.Value!.Status);
        Assert.Equal(expected, (await s.FindAsync(created.Id))!.Status);
    }

    [Fact]
    public async Task Only_pending_requests_can_be_decided()
    {
        using var db = TestDb.Create();
        var s = new LeaveService(db);
        var approved = await Ok(s, Farah, Req("2026-12-07", "2026-12-07"));
        var res = await s.DecideAsync((await s.FindAsync(approved.Id))!, approve: false);
        Assert.Contains("pending", res.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(LeaveStatus.Approved, (await s.FindAsync(approved.Id))!.Status);
    }

    [Fact]
    public async Task Unknown_request_is_not_found()
    {
        using var db = TestDb.Create();
        Assert.Null(await new LeaveService(db).FindAsync(99999));
    }
}
