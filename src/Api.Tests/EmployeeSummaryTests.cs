using System.Net;
using System.Net.Http.Json;
using Api.Data;
using Api.Models;
using Api.Services;

namespace Api.Tests;

/// <summary>Days taken this year: only Approved leave counts, and the year is the request's start year.</summary>
public class DaysTakenThisYearTests
{
    // Employee 9 has no seeded leave, so each test controls everything that is counted.
    const int Hana = 9;
    static readonly int Year = DateTime.UtcNow.Year;
    static readonly DateOnly June = Seed.MondayOnOrAfter(new DateOnly(Year, 6, 1));

    static async Task<decimal?> DaysTakenWith(params LeaveRequest[] requests)
    {
        using var db = TestDb.Create();
        db.LeaveRequests.AddRange(requests);
        db.SaveChanges();
        return await new LeaveService(db).DaysTakenThisYearAsync(Hana);
    }

    static LeaveRequest Leave(DateOnly start, DateOnly end, LeaveStatus status, HalfDay half = HalfDay.None) =>
        new() { EmployeeId = Hana, StartDate = start, EndDate = end, Status = status, HalfDay = half };

    [Fact]
    public async Task Approved_leave_counts() =>
        Assert.Equal(3m, await DaysTakenWith(Leave(June, June.AddDays(2), LeaveStatus.Approved)));

    [Fact]
    public async Task Pending_leave_does_not_count() =>
        Assert.Equal(0m, await DaysTakenWith(Leave(June, June.AddDays(4), LeaveStatus.Pending)));

    [Fact]
    public async Task Rejected_leave_does_not_count() =>
        Assert.Equal(0m, await DaysTakenWith(Leave(June, June.AddDays(4), LeaveStatus.Rejected)));

    [Fact]
    public async Task Only_approved_counts_when_statuses_are_mixed() =>
        Assert.Equal(3m, await DaysTakenWith(
            Leave(June, June.AddDays(2), LeaveStatus.Approved),
            Leave(June.AddDays(7), June.AddDays(11), LeaveStatus.Pending),
            Leave(June.AddDays(14), June.AddDays(18), LeaveStatus.Rejected)));

    [Fact]
    public async Task Approved_half_day_counts_as_half() =>
        Assert.Equal(0.5m, await DaysTakenWith(Leave(June, June, LeaveStatus.Approved, HalfDay.AM)));

    [Fact]
    public async Task Leave_that_started_last_year_does_not_count()
    {
        var lastYear = Seed.MondayOnOrAfter(new DateOnly(Year - 1, 6, 1));
        Assert.Equal(0m, await DaysTakenWith(Leave(lastYear, lastYear.AddDays(2), LeaveStatus.Approved)));
    }

    [Fact]
    public async Task Leave_from_late_December_counts_fully_in_its_start_year()
    {
        // Mon 29 Dec of this year at the latest: 5 working days, some of them in January.
        var lateDec = Seed.MondayOnOrAfter(new DateOnly(Year, 12, 22));
        Assert.Equal(5m, await DaysTakenWith(Leave(lateDec, lateDec.AddDays(4), LeaveStatus.Approved)));
    }
}

/// <summary>GET /employees/{id}/summary over HTTP: access via AccessService, 403 for others, 404 for unknown ids.</summary>
public class EmployeeSummaryApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    record Summary(int EmployeeId, decimal DaysTakenThisYear);

    [Fact]
    public async Task Employee_sees_their_own_summary()
    {
        // Seed: Farah (6) has an approved Mon-Wed trip this year.
        var farah = await api.SignedInAs(People.Farah);
        var res = await farah.GetAsync("/employees/6/summary");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(new Summary(6, 3m), await res.Content.ReadFromJsonAsync<Summary>());
    }

    [Fact]
    public async Task Manager_sees_a_direct_reports_summary()
    {
        var aina = await api.SignedInAs(People.Aina);
        Assert.Equal(HttpStatusCode.OK, (await aina.GetAsync("/employees/6/summary")).StatusCode);
    }

    [Fact]
    public async Task Another_employee_gets_403()
    {
        var farah = await api.SignedInAs(People.Farah);
        Assert.Equal(HttpStatusCode.Forbidden, (await farah.GetAsync("/employees/7/summary")).StatusCode);
    }

    [Fact]
    public async Task Unknown_employee_id_gets_404()
    {
        var hr = await api.SignedInAs(People.Dina);
        Assert.Equal(HttpStatusCode.NotFound, (await hr.GetAsync("/employees/99999/summary")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_caller_gets_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/employees/6/summary")).StatusCode);
    }
}
