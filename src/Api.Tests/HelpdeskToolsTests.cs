using System.Text.Json;
using Api.Helpdesk;
using Api.Services;
using Xunit;

namespace Api.Tests;

public class HelpdeskToolsTests
{
    [Fact]
    public async Task Any_tool_failure_comes_back_as_an_error_result_not_an_exception()
    {
        var db = TestDb.Create();
        var tools = new HelpdeskTools(db, new LeaveService(db));
        var input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"start_date": 5, "end_date": 6}""")!;
        var outcome = await tools.RunAsync("apply_leave", input, 6);
        Assert.True(outcome.IsError);
    }

    // The 3-working-day rule (POL-02) is enforced by apply_leave in C#, whatever the model or the prompt says.
    // Farah (6). Dates: Mon 2026-12-07 ... Fri 2026-12-11, Mon 2026-12-14.
    static async Task<(JsonElement Result, Api.Data.AcmeDb Db)> Apply(string start, string end, string halfDay = "None")
    {
        var db = TestDb.Create();
        var tools = new HelpdeskTools(db, new LeaveService(db));
        var input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            JsonSerializer.Serialize(new { start_date = start, end_date = end, half_day = halfDay, reason = "Trip" }))!;
        var outcome = await tools.RunAsync("apply_leave", input, 6);
        Assert.False(outcome.IsError, outcome.Json);
        return (JsonDocument.Parse(outcome.Json).RootElement.Clone(), db);
    }

    static Api.Models.LeaveRequest Row(Api.Data.AcmeDb db, JsonElement result) =>
        db.LeaveRequests.Single(r => r.Id == result.GetProperty("request_id").GetInt32());

    [Fact]
    public async Task Four_working_days_need_manager_approval_and_stay_pending()
    {
        var (result, db) = await Apply("2026-12-07", "2026-12-10");
        Assert.Equal("needs_manager_approval", result.GetProperty("status").GetString());
        Assert.Equal(4, result.GetProperty("days").GetDecimal());
        Assert.Equal(Api.Models.LeaveStatus.Pending, Row(db, result).Status);
    }

    [Fact]
    public async Task Three_working_days_are_approved()
    {
        var (result, db) = await Apply("2026-12-07", "2026-12-09");
        Assert.Equal("approved", result.GetProperty("status").GetString());
        Assert.Equal(Api.Models.LeaveStatus.Approved, Row(db, result).Status);
    }

    [Fact]
    public async Task Weekend_days_do_not_count_toward_the_three_day_rule()
    {
        var (result, _) = await Apply("2026-12-10", "2026-12-14"); // Thu, Fri, (Sat, Sun), Mon = 3 working days
        Assert.Equal("approved", result.GetProperty("status").GetString());
        Assert.Equal(3, result.GetProperty("days").GetDecimal());
    }

    [Fact]
    public async Task Half_day_is_approved()
    {
        var (result, _) = await Apply("2026-12-07", "2026-12-07", "PM");
        Assert.Equal("approved", result.GetProperty("status").GetString());
        Assert.Equal(0.5m, result.GetProperty("days").GetDecimal());
    }
}
