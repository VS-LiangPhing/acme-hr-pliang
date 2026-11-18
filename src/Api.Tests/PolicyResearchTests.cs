using Api.Research;
using Api.Services;

namespace Api.Tests;

public class PolicyResearchTests
{
    static PolicyResearch Make(FakeClaude fake) => new(fake, TestDb.Create(), new ClaudeOptions("claude-sonnet-5-5"));

    [Fact]
    public void Validator_flags_uncited_sentence()
    {
        var bad = PolicyResearch.UncitedSentences("You can carry forward 5 days [POL-04]. Half days are fine.");
        Assert.Single(bad);
    }

    [Fact]
    public void Validator_accepts_all_cited()
    {
        Assert.Empty(PolicyResearch.UncitedSentences("You can carry forward 5 days [POL-04]. Half days deduct 0.5 [POL-03, POL-04]."));
    }

    [Fact]
    public async Task Lead_runs_a_worker_and_returns_cited_answer()
    {
        var fake = new FakeClaude(
            FakeClaude.ToolUse("l1", "research", new { subtopic = "carry forward" }),   // lead
            FakeClaude.ToolUse("w1", "search_policies", new { query = "carry" }),     // worker
            FakeClaude.Text("Up to 5 days by 31 March [POL-04]"),                      // worker done
            FakeClaude.Text("You can carry forward up to 5 days [POL-04]."));          // lead answer
        var r = await Make(fake).AskAsync("Can I carry forward leave?");
        Assert.True(r.CitationsValid);
        Assert.Contains(r.Agents, a => a.Agent == "worker-1");
        Assert.True(r.TotalTokens > 0);
    }

    [Fact]
    public async Task Worker_limit_is_enforced()
    {
        var calls = Enumerable.Range(1, 5).Select(i => FakeClaude.ToolUse($"l{i}", "research", new { subtopic = $"t{i}" })).ToList();
        var replies = new List<string>();
        foreach (var c in calls) { replies.Add(c); replies.Add(FakeClaude.Text("finding [POL-01]")); }
        var fake = new FakeClaude([.. replies, FakeClaude.Text("Done [POL-01].")]);
        var r = await Make(fake).AskAsync("Many topics");
        Assert.True(r.Agents.Count(a => a.Agent.StartsWith("worker")) <= PolicyResearch.MaxWorkers);
    }

    [Fact]
    public void Citation_after_the_full_stop_counts()
    {
        Assert.Empty(PolicyResearch.UncitedSentences("You can carry forward 5 days. [POL-04] Half days deduct 0.5. [POL-03]"));
        Assert.Equal(["Yes, you can do both."], PolicyResearch.UncitedSentences("Yes, you can do both. Half days deduct 0.5. [POL-03]"));
    }

    [Fact]
    public void Citation_before_the_full_stop_counts() =>
        Assert.Empty(PolicyResearch.UncitedSentences("You can carry forward 5 days [POL-04]. Half days deduct 0.5 [POL-03]."));

    [Fact]
    public async Task Uncited_answer_gets_one_repair_turn()
    {
        var fake = new FakeClaude(
            FakeClaude.Text("Yes, you can do both. Half days deduct 0.5 [POL-03]."),
            FakeClaude.Text("You can do both [POL-03, POL-04]. Half days deduct 0.5 [POL-03]."));
        var r = await Make(fake).AskAsync("Can I carry forward leave and take it as half days?");
        Assert.True(r.CitationsValid);
        Assert.Equal(2, fake.Requests.Count);
        var last = fake.Requests[1];
        Assert.Contains("Yes, you can do both.", FakeClaude.MessageJson(last, last.Messages.Count - 1).ToString());
    }

    [Fact]
    public async Task Repair_happens_only_once()
    {
        var fake = new FakeClaude(FakeClaude.Text("Yes. You can."), FakeClaude.Text("Yes. You still can."), FakeClaude.Text("Never sent [POL-01]."));
        var r = await Make(fake).AskAsync("Can I?");
        Assert.False(r.CitationsValid);
        Assert.Equal(2, fake.Requests.Count);
    }

    [Fact]
    public async Task Cited_answer_needs_no_repair()
    {
        var fake = new FakeClaude(FakeClaude.Text("You get 14 days [POL-01]."));
        var r = await Make(fake).AskAsync("How many days?");
        Assert.True(r.CitationsValid);
        Assert.Single(fake.Requests);
    }
}

// RunWorker: a separate conversation with two tools, its own turn limit, and the shared token budget.
// FakeClaude reports 100 input + 20 output tokens per reply unless a test changes it.
public class PolicyResearchWorkerTests
{
    static PolicyResearch Make(FakeClaude fake) => new(fake, TestDb.Create(), new ClaudeOptions("claude-sonnet-5-5"));

    static AgentUsage Usage(ResearchResult r, string agent) => Assert.Single(r.Agents, a => a.Agent == agent);

    static List<string> ToolJson(Anthropic.Models.Messages.MessageCreateParams req) =>
        req.Tools!.Select(t => System.Text.Json.JsonSerializer.Serialize(t)).ToList();

    /// <summary>A scripted reply that reports more input tokens than FakeClaude's default 100.</summary>
    static string WithInputTokens(string reply, long tokens) => reply.Replace("\"input_tokens\":100", $"\"input_tokens\":{tokens}");

    [Fact]
    public async Task Worker_answers_with_findings_in_its_own_conversation()
    {
        var fake = new FakeClaude(
            FakeClaude.ToolUse("l1", "research", new { subtopic = "carry forward" }),        // lead, request 0
            FakeClaude.ToolUse("w1", "search_policies", new { query = "carry forward" }),   // worker, request 1
            FakeClaude.ToolUse("w2", "read_policy", new { id = "POL-04" }),                 // worker, request 2
            FakeClaude.Text("Up to 5 days carry forward, used by 31 March [POL-04]"),       // worker, request 3
            FakeClaude.Text("You can carry forward up to 5 days [POL-04]."));               // lead, request 4
        var r = await Make(fake).AskAsync("Can I carry forward leave, and how much?");

        // The worker starts fresh: only its subtopic, and only the two policy tools.
        var first = fake.Requests[1];
        Assert.Single(first.Messages);
        Assert.Equal("carry forward", FakeClaude.MessageJson(first, 0).GetProperty("content").GetString());
        var tools = ToolJson(first);
        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, t => t.Contains("search_policies"));
        Assert.Contains(tools, t => t.Contains("read_policy"));
        Assert.DoesNotContain(tools, t => t.Contains("\"research\""));

        // Its tools ran: one result per tool_use, from the real policy table.
        Assert.Contains("POL-04", Assert.Single(FakeClaude.ToolResults(fake.Requests[2])).Content);
        Assert.Contains("Carry forward", Assert.Single(FakeClaude.ToolResults(fake.Requests[3])).Content);

        // The lead gets the findings as a normal (not error) tool_result.
        var toLead = Assert.Single(FakeClaude.ToolResults(fake.Requests[4]));
        Assert.Equal("l1", toLead.ToolUseId);
        Assert.False(toLead.IsError);
        Assert.Equal("Up to 5 days carry forward, used by 31 March [POL-04]", toLead.Content);

        // Tokens per agent.
        Assert.Equal(new AgentUsage("worker-1", 3, 300, 60), Usage(r, "worker-1"));
        Assert.Equal(new AgentUsage("lead", 2, 200, 40), Usage(r, "lead"));
        Assert.Equal(600, r.TotalTokens);
        Assert.True(r.CitationsValid);
    }

    [Fact]
    public async Task Worker_stops_at_its_turn_limit_and_the_lead_gets_an_error()
    {
        var workerLoop = Enumerable.Range(1, PolicyResearch.MaxTurnsPerWorker)
            .Select(i => FakeClaude.ToolUse($"w{i}", "search_policies", new { query = "leave" }));
        var fake = new FakeClaude([
            FakeClaude.ToolUse("l1", "research", new { subtopic = "everything about leave" }),
            .. workerLoop,
            FakeClaude.Text("The policies I checked did not answer this [POL-01]."),
        ]);
        var r = await Make(fake).AskAsync("Tell me everything about leave");

        Assert.Equal(1 + PolicyResearch.MaxTurnsPerWorker + 1, fake.Requests.Count); // the worker sent no 7th request
        Assert.Equal(PolicyResearch.MaxTurnsPerWorker, Usage(r, "worker-1").Turns);

        var toLead = Assert.Single(FakeClaude.ToolResults(fake.Requests[^1]));
        Assert.Equal("l1", toLead.ToolUseId);
        Assert.True(toLead.IsError);
        Assert.StartsWith("Stopped:", toLead.Content);
        Assert.Contains("turn limit", toLead.Content);
        Assert.Equal("end_turn", r.StoppedBy);
    }

    [Fact]
    public async Task Worker_stops_when_the_token_budget_is_used_up()
    {
        var fake = new FakeClaude(
            FakeClaude.ToolUse("l1", "research", new { subtopic = "medical leave" }),                                   // lead: 120 tokens
            WithInputTokens(FakeClaude.ToolUse("w1", "search_policies", new { query = "medical" }), PolicyResearch.TokenBudget), // worker: over budget
            FakeClaude.Text("never sent: worker turn 2"),
            FakeClaude.Text("never sent: lead turn 2"));
        var r = await Make(fake).AskAsync("What about medical leave?");

        Assert.Equal(2, fake.Requests.Count); // the worker stopped before its 2nd turn, the lead before its 2nd turn
        Assert.Equal(new AgentUsage("worker-1", 1, PolicyResearch.TokenBudget, 20), Usage(r, "worker-1"));
        var lead = Usage(r, "lead"); // lead Turns is not checked: the lead loop counts the turn it skipped on the budget (existing lead code)
        Assert.Equal((100, 20), (lead.InputTokens, lead.OutputTokens));
        Assert.Equal(PolicyResearch.TokenBudget + 140, r.TotalTokens);
        Assert.Equal("token_budget", r.StoppedBy);
        Assert.False(r.CitationsValid);
    }
}
