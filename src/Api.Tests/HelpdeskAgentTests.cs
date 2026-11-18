using Anthropic.Models.Messages;
using Api.Helpdesk;
using Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests;

public class HelpdeskAgentTests
{
    static (HelpdeskAgent agent, FakeClaude fake) Make(params string[] replies)
    {
        var db = TestDb.Create();
        var fake = new FakeClaude(replies);
        return (new HelpdeskAgent(fake, new HelpdeskTools(db, new LeaveService(db)), new ClaudeOptions("claude-sonnet-5-5")), fake);
    }

    static readonly List<ChatTurn> Ask = [new("user", "How many days do I have?")];

    [Fact]
    public async Task Plain_answer_ends_after_one_turn()
    {
        var (agent, _) = Make(FakeClaude.Text("You have 14 days."));
        var r = await agent.RunAsync(1, Ask);
        Assert.Equal("You have 14 days.", r.Reply);
        Assert.Equal(1, r.Turns);
    }

    [Fact]
    public async Task Tool_call_then_answer()
    {
        var (agent, fake) = Make(FakeClaude.ToolUse("t1", "get_leave_balance", new { }), FakeClaude.Text("You have 14 days."));
        var r = await agent.RunAsync(1, Ask);
        Assert.Single(r.ToolCalls);
        Assert.Equal(2, r.Turns);
        // Second request resends history: user, assistant (tool_use), user (tool_result)
        Assert.Equal(3, fake.Requests[1].Messages.Count);
    }

    [Fact]
    public async Task Five_day_leave_needs_manager_approval_from_code()
    {
        var (agent, _) = Make(
            FakeClaude.ToolUse("t1", "apply_leave", new { start_date = "2026-11-16", end_date = "2026-11-20", half_day = "None" }),
            FakeClaude.Text("Sent to your manager."));
        var r = await agent.RunAsync(1, Ask);
        Assert.Contains("needs_manager_approval", r.ToolCalls[0].Output);
    }

    [Fact]
    public async Task Tool_error_is_returned_not_thrown()
    {
        var (agent, _) = Make(
            FakeClaude.ToolUse("t1", "apply_leave", new { start_date = "16/11/2026", end_date = "2026-11-20" }),
            FakeClaude.Text("Please use yyyy-MM-dd."));
        var r = await agent.RunAsync(1, Ask);
        Assert.True(r.ToolCalls[0].IsError);
    }

    [Fact]
    public async Task Stops_at_turn_limit()
    {
        var loop = Enumerable.Range(0, 20).Select(i => FakeClaude.ToolUse($"t{i}", "get_leave_balance", new { })).ToArray();
        var (agent, _) = Make(loop);
        var r = await agent.RunAsync(1, Ask);
        Assert.Equal(HelpdeskAgent.MaxTurns, r.Turns);
        Assert.Equal("turn_limit", r.StopReason);
    }
}

// The loop's contract, checked through what FakeClaude saw (its Requests) and the tool_results we sent back.
public class HelpdeskAgentLoopTests
{
    static (HelpdeskAgent agent, FakeClaude fake, Api.Data.AcmeDb db) Make(params string[] replies)
    {
        var db = TestDb.Create();
        var fake = new FakeClaude(replies);
        return (new HelpdeskAgent(fake, new HelpdeskTools(db, new LeaveService(db)), new ClaudeOptions("claude-sonnet-5-5")), fake, db);
    }

    static readonly List<ChatTurn> Ask = [new("user", "How many days do I have?")];

    [Fact]
    public async Task Each_request_is_a_snapshot_of_the_history_at_that_turn()
    {
        var (agent, fake, _) = Make(FakeClaude.ToolUse("t1", "get_leave_balance", new { }), FakeClaude.Text("You have 14 days."));
        await agent.RunAsync(1, Ask);
        Assert.Equal(2, fake.Requests.Count);
        Assert.Single(fake.Requests[0].Messages);        // a live list would show 3 here too
        Assert.Equal(3, fake.Requests[1].Messages.Count); // user, assistant tool_use, user tool_result
    }

    [Fact]
    public async Task Full_history_is_resent_every_turn()
    {
        var (agent, fake, _) = Make(
            FakeClaude.ToolUse("t1", "get_leave_balance", new { }),
            FakeClaude.ToolUse("t2", "check_policy", new { topic = "carry forward" }),
            FakeClaude.Text("Done."));
        List<ChatTurn> chat = [new("user", "Hi"), new("assistant", "Hello, how can I help?"), new("user", "My balance and carry forward?")];
        var r = await agent.RunAsync(1, chat);
        Assert.Equal(3, r.Turns);
        Assert.Equal([3, 5, 7], fake.Requests.Select(q => q.Messages.Count));
        Assert.Equal("t1", Assert.Single(FakeClaude.ToolResults(fake.Requests[1])).ToolUseId);
        Assert.Equal("t2", Assert.Single(FakeClaude.ToolResults(fake.Requests[2])).ToolUseId);
    }

    [Fact]
    public async Task Every_tool_use_gets_one_result_in_one_user_message()
    {
        var (agent, fake, _) = Make(
            FakeClaude.ToolUses(("t1", "get_leave_balance", new { }), ("t2", "check_policy", new { topic = "carry forward" })),
            FakeClaude.Text("You have 14 days, and carry forward is POL-04."));
        var r = await agent.RunAsync(1, Ask);
        Assert.Equal(2, r.ToolCalls.Count);
        Assert.Equal(3, fake.Requests[1].Messages.Count); // both results in ONE message
        var results = FakeClaude.ToolResults(fake.Requests[1]);
        Assert.Equal(["t1", "t2"], results.Select(x => x.ToolUseId));
        Assert.All(results, x => Assert.False(x.IsError));
    }

    [Fact]
    public async Task Unknown_tool_gets_an_error_result_and_the_loop_goes_on()
    {
        var (agent, fake, _) = Make(FakeClaude.ToolUse("t1", "delete_everything", new { }), FakeClaude.Text("I can't do that."));
        var r = await agent.RunAsync(1, Ask);
        var result = Assert.Single(FakeClaude.ToolResults(fake.Requests[1]));
        Assert.Equal("t1", result.ToolUseId);
        Assert.True(result.IsError);
        Assert.Equal("I can't do that.", r.Reply);
    }

    [Fact]
    public async Task A_tool_that_throws_still_gets_an_error_result()
    {
        var (agent, fake, db) = Make(
            FakeClaude.ToolUses(("t1", "get_leave_balance", new { }), ("t2", "check_policy", new { topic = "medical" })),
            FakeClaude.Text("Something went wrong, HR will help."));
        // Closing the in-memory connection drops the database: the next query throws a SqliteException
        // that HelpdeskTools does not catch, so the agent loop has to.
        db.Database.GetDbConnection().Close();

        var r = await agent.RunAsync(1, Ask);
        Assert.Equal("end_turn", r.StopReason);
        var results = FakeClaude.ToolResults(fake.Requests[1]);
        Assert.Equal(["t1", "t2"], results.Select(x => x.ToolUseId));
        Assert.All(results, x => Assert.True(x.IsError));
        Assert.All(r.ToolCalls, c => Assert.True(c.IsError));
    }

    [Fact]
    public async Task Other_stop_reasons_end_the_loop_and_are_reported()
    {
        var (agent, fake, _) = Make(FakeClaude.Text("You have", stop: "max_tokens"));
        var r = await agent.RunAsync(1, Ask);
        Assert.Equal("max_tokens", r.StopReason);
        Assert.Equal(1, r.Turns);
        Assert.Equal("You have", r.Reply);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Turn_limit_sends_exactly_MaxTurns_requests_and_hands_over_to_HR()
    {
        var loop = Enumerable.Range(0, 20).Select(i => FakeClaude.ToolUse($"t{i}", "get_leave_balance", new { })).ToArray();
        var (agent, fake, _) = Make(loop);
        var r = await agent.RunAsync(1, Ask);
        Assert.Equal(HelpdeskAgent.MaxTurns, fake.Requests.Count);
        Assert.Equal(HelpdeskAgent.MaxTurns, r.ToolCalls.Count);
        Assert.Contains("HR officer", r.Reply);
        for (var i = 1; i < fake.Requests.Count; i++) // every tool_use was answered before the next call
            Assert.Equal($"t{i - 1}", Assert.Single(FakeClaude.ToolResults(fake.Requests[i])).ToolUseId);
    }

    [Fact]
    public async Task Tokens_are_summed_over_all_turns()
    {
        var (agent, _, _) = Make(FakeClaude.ToolUse("t1", "get_leave_balance", new { }), FakeClaude.Text("You have 14 days."));
        var r = await agent.RunAsync(1, Ask);
        Assert.Equal(200, r.InputTokens);  // FakeClaude: 100 in, 20 out per reply
        Assert.Equal(40, r.OutputTokens);
    }

    [Fact]
    public async Task Request_carries_model_tools_system_and_wrapped_user_text()
    {
        var (agent, fake, _) = Make(FakeClaude.Text("Hi."));
        await agent.RunAsync(1, Ask);
        var req = fake.Requests[0];
        Assert.Equal("claude-sonnet-5-5", req.Model.Raw());
        Assert.Equal(HelpdeskAgent.MaxTokens, req.MaxTokens);
        Assert.Equal(HelpdeskTools.Definitions.Count, req.Tools!.Count);
        Assert.Equal("<message>How many days do I have?</message>", FakeClaude.MessageJson(req, 0).GetProperty("content").GetString());
    }
}
