using System.Net;
using System.Net.Http.Json;

namespace Api.Tests;

// POST /leave, /leave/{id}/approve and /leave/{id}/reject over HTTP (docs/feature-spec-leave.md).
// This class has its own ApiFactory, so its own database. Tests share it, so each test uses its own dates.
// Farah (6) reports to Aina (1). Gopal (7) is on another team.
public class LeaveApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    record Row(int Id, int EmployeeId, string Status, decimal Days);
    record Problem(int Status, string Title);

    static object Body(string start, string end, string halfDay = "None", string reason = "Trip") =>
        new { startDate = start, endDate = end, halfDay, reason };

    async Task<Row> Create(HttpClient http, object body)
    {
        var res = await http.PostAsJsonAsync("/leave", body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<Row>())!;
    }

    [Fact]
    public async Task Create_uses_the_signed_in_employee_and_ignores_employeeId_in_the_body()
    {
        var farah = await api.SignedInAs(People.Farah);
        var row = await Create(farah, new { startDate = "2027-02-01", endDate = "2027-02-01", halfDay = "None", reason = "Trip", employeeId = 7 });
        Assert.Equal(6, row.EmployeeId);
        Assert.Equal(1, row.Days);
        Assert.Equal("Approved", row.Status);
    }

    [Fact]
    public async Task Missing_start_date_is_a_400_problem()
    {
        var farah = await api.SignedInAs(People.Farah);
        var res = await farah.PostAsJsonAsync("/leave", new { endDate = "2027-02-02", halfDay = "None", reason = "Trip" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("startDate", (await res.Content.ReadFromJsonAsync<Problem>())!.Title);
    }

    [Fact]
    public async Task Weekend_only_request_is_a_400()
    {
        var farah = await api.SignedInAs(People.Farah);
        Assert.Equal(HttpStatusCode.BadRequest, (await farah.PostAsJsonAsync("/leave", Body("2027-02-06", "2027-02-07"))).StatusCode);
    }

    [Fact]
    public async Task Manager_approves_a_pending_request()
    {
        var pending = await Create(await api.SignedInAs(People.Farah), Body("2027-03-01", "2027-03-04"));
        Assert.Equal("Pending", pending.Status);

        var res = await (await api.SignedInAs(People.Aina)).PostAsync($"/leave/{pending.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Approved", (await res.Content.ReadFromJsonAsync<Row>())!.Status);
    }

    [Fact]
    public async Task Manager_rejects_a_pending_request()
    {
        var pending = await Create(await api.SignedInAs(People.Farah), Body("2027-03-08", "2027-03-11"));
        var res = await (await api.SignedInAs(People.Aina)).PostAsync($"/leave/{pending.Id}/reject", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Rejected", (await res.Content.ReadFromJsonAsync<Row>())!.Status);
    }

    [Fact]
    public async Task Requester_and_other_employees_get_403()
    {
        var farah = await api.SignedInAs(People.Farah);
        var pending = await Create(farah, Body("2027-03-15", "2027-03-18"));
        var gopal = await api.SignedInAs(People.Gopal);

        Assert.Equal(HttpStatusCode.Forbidden, (await farah.PostAsync($"/leave/{pending.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gopal.PostAsync($"/leave/{pending.Id}/approve", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gopal.PostAsync($"/leave/{pending.Id}/reject", null)).StatusCode);
    }

    [Fact]
    public async Task Not_pending_is_403_for_outsiders_and_400_for_the_manager()
    {
        var approved = await Create(await api.SignedInAs(People.Farah), Body("2027-02-08", "2027-02-08"));
        Assert.Equal("Approved", approved.Status);

        var gopal = await api.SignedInAs(People.Gopal);
        Assert.Equal(HttpStatusCode.Forbidden, (await gopal.PostAsync($"/leave/{approved.Id}/reject", null)).StatusCode);
        var aina = await api.SignedInAs(People.Aina);
        Assert.Equal(HttpStatusCode.BadRequest, (await aina.PostAsync($"/leave/{approved.Id}/reject", null)).StatusCode);
    }

    [Fact]
    public async Task Unknown_request_is_404()
    {
        var aina = await api.SignedInAs(People.Aina);
        Assert.Equal(HttpStatusCode.NotFound, (await aina.PostAsync("/leave/999999/approve", null)).StatusCode);
    }

    [Fact]
    public async Task Leave_list_includes_working_days()
    {
        var farah = await api.SignedInAs(People.Farah);
        await Create(farah, Body("2027-02-15", "2027-02-15", "PM"));
        var rows = await farah.GetFromJsonAsync<List<Row>>("/leave");
        Assert.Contains(rows!, r => r.Days == 0.5m);
        Assert.All(rows!, r => Assert.True(r.Days > 0));
    }
}
