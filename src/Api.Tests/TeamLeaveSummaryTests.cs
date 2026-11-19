using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Api.Services;

namespace Api.Tests;

// Seed: manager 1 Aina has direct reports 6, 11, 16, 21, 26, 31, 36. Farah (6) has 3 approved days.
// Manager 2 Ben's report Gopal (7) has an approved half day. Manager 3 Chen's report 8 has 5 pending days.
public class TeamLeaveSummaryServiceTests
{
    [Fact]
    public async Task Sums_approved_days_of_direct_reports_only()
    {
        using var db = TestDb.Create();
        var s = await new LeaveService(db).TeamSummaryAsync(1, null);

        Assert.Equal(1, s.ManagerId);
        Assert.Equal(7, s.DirectReports);
        Assert.Equal(3m, s.DaysTaken); // not Gopal's 0.5: he reports to Ben
    }

    [Fact]
    public async Task Half_days_count_as_half()
    {
        using var db = TestDb.Create();
        Assert.Equal(0.5m, (await new LeaveService(db).TeamSummaryAsync(2, null)).DaysTaken);
    }

    [Fact]
    public async Task Pending_and_rejected_leave_is_not_taken()
    {
        using var db = TestDb.Create();
        var mon = Api.Data.Seed.MondayOnOrAfter(new DateOnly(2030, 6, 3));
        db.LeaveRequests.Add(new LeaveRequest { EmployeeId = 13, StartDate = mon, EndDate = mon.AddDays(1), Status = LeaveStatus.Rejected });
        db.SaveChanges();

        Assert.Equal(0m, (await new LeaveService(db).TeamSummaryAsync(3, null)).DaysTaken); // 8 pending, 13 rejected
    }

    [Fact]
    public async Task Year_filter_counts_only_days_inside_that_year()
    {
        using var db = TestDb.Create();
        // Mon 30 Dec 2030 to Fri 3 Jan 2031: 2 working days in 2030, 3 in 2031.
        db.LeaveRequests.Add(new LeaveRequest { EmployeeId = 11, StartDate = new DateOnly(2030, 12, 30), EndDate = new DateOnly(2031, 1, 3), Status = LeaveStatus.Approved });
        db.SaveChanges();
        var leave = new LeaveService(db);

        Assert.Equal(2m, (await leave.TeamSummaryAsync(1, 2030)).DaysTaken);
        Assert.Equal(3m, (await leave.TeamSummaryAsync(1, 2031)).DaysTaken);
        Assert.Equal(3m, (await leave.TeamSummaryAsync(1, DateTime.UtcNow.Year)).DaysTaken); // Farah's seeded trip
        Assert.Equal(2031, (await leave.TeamSummaryAsync(1, 2031)).Year);
    }

    [Fact]
    public async Task Manager_with_no_reports_has_zero()
    {
        using var db = TestDb.Create();
        var s = await new LeaveService(db).TeamSummaryAsync(40, null);
        Assert.Equal(0, s.DirectReports);
        Assert.Equal(0m, s.DaysTaken);
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public async Task Year_out_of_range_throws(int year)
    {
        using var db = TestDb.Create();
        await Assert.ThrowsAsync<LeaveException>(() => new LeaveService(db).TeamSummaryAsync(1, year));
    }
}

public class TeamLeaveSummaryApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    record Summary(int ManagerId, int? Year, int DirectReports, decimal DaysTaken);

    [Fact]
    public async Task Manager_gets_the_total_for_their_direct_reports()
    {
        var aina = await api.SignedInAs(People.Aina);
        var res = await aina.GetAsync("/leave/team-summary");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var s = await res.Content.ReadFromJsonAsync<Summary>();
        Assert.Equal(new Summary(1, null, 7, 3m), s);
    }

    [Fact]
    public async Task Manager_is_taken_from_the_token_not_the_query()
    {
        var aina = await api.SignedInAs(People.Aina);
        var s = await aina.GetFromJsonAsync<Summary>("/leave/team-summary?managerId=2");
        Assert.Equal(1, s!.ManagerId);
    }

    [Fact]
    public async Task Employee_who_is_not_a_manager_gets_403()
    {
        var farah = await api.SignedInAs(People.Farah);
        Assert.Equal(HttpStatusCode.Forbidden, (await farah.GetAsync("/leave/team-summary")).StatusCode);
    }

    [Fact]
    public async Task Year_out_of_range_is_400()
    {
        var aina = await api.SignedInAs(People.Aina);
        Assert.Equal(HttpStatusCode.BadRequest, (await aina.GetAsync("/leave/team-summary?year=1990")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_caller_is_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/leave/team-summary")).StatusCode);
    }
}
