using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropic.Models.Messages;
using Api.Data;
using Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Api.Research;

public record AgentUsage(string Agent, int Turns, long InputTokens, long OutputTokens);
public record ResearchResult(string Answer, bool CitationsValid, IReadOnlyList<string> UncitedSentences, IReadOnlyList<AgentUsage> Agents, long TotalTokens, string StoppedBy);

/// <summary>
/// Lab 3: a lead agent splits a policy question and hands each subtopic to a worker.
/// Each worker is a separate Claude conversation with its own context and only two tools.
/// </summary>
public class PolicyResearch(IClaudeChat claude, AcmeDb db, ClaudeOptions options)
{
    // Hard limits, in code.
    public const int MaxWorkers = 3;
    public const int MaxTurnsLead = 6;
    public const int MaxTurnsPerWorker = 6;
    public const long TokenBudget = 80_000;

    readonly List<AgentUsage> _usage = [];
    long _tokens;

    const string LeadPrompt = """
        You lead a small research team answering questions about Acme HR policies.
        1. Split the question into 2 or 3 subtopics and call research once per subtopic.
        2. When the findings are back, write one answer for the employee, under 150 words.
        3. EVERY sentence, including a first yes or no, ends with the policy ids it relies on, before the full stop: "Staff get 14 days [POL-01]." Never state anything without a policy id. If the policies do not cover something, say so in one sentence with the ids you checked.
        4. Plain text only: no markdown, no ** and no "- " bullets. The page shows your answer as it is.
        """;

    const string WorkerPrompt = """
        You research one subtopic in the Acme HR policies. Use search_policies, then read_policy for the relevant ones.
        Reply with short findings, each line ending with its policy id in brackets, like [POL-04]. Only report what the policies say.
        """;

    static readonly IReadOnlyList<ToolUnion> LeadTools =
    [
        Tool("research", "Give one subtopic to a research worker. Returns findings with policy ids.", new { subtopic = new { type = "string" } }, "subtopic"),
    ];

    static readonly IReadOnlyList<ToolUnion> WorkerTools =
    [
        Tool("search_policies", "Search policies by keyword. Returns ids and titles.", new { query = new { type = "string" } }, "query"),
        Tool("read_policy", "Read one policy by id, e.g. POL-04.", new { id = new { type = "string" } }, "id"),
    ];

    public async Task<ResearchResult> AskAsync(string question, CancellationToken ct = default)
    {
        var history = new List<MessageParam> { new UserMessageParam { Content = question } };
        var workers = 0;
        int turns = 0; long inTok = 0, outTok = 0;
        string answer = "", stoppedBy = "end_turn";

        for (turns = 1; turns <= MaxTurnsLead; turns++)
        {
            if (_tokens >= TokenBudget) { stoppedBy = "token_budget"; break; }
            var res = await claude.SendAsync(new MessageCreateParams { Model = options.Model, MaxTokens = 1500, System = LeadPrompt, Tools = LeadTools, Messages = history.ToList() }, ct);
            inTok += res.Usage.InputTokens; outTok += res.Usage.OutputTokens; _tokens += res.Usage.InputTokens + res.Usage.OutputTokens;
            history.Add(new AssistantMessageParam { Content = res.Content.Select(b => new ContentBlockParam(b.Json)).ToList() });

            if (res.StopReason != StopReason.ToolUse) { answer = TextOf(res); break; }

            var results = new List<ContentBlockParam>();
            foreach (var block in res.Content)
            {
                if (!block.TryPickToolUse(out var call)) continue;
                string output; bool isError = false;
                if (workers >= MaxWorkers) { output = $"Worker limit reached ({MaxWorkers}). Answer with what you have."; isError = true; }
                else if (_tokens >= TokenBudget) { output = "Token budget used up. Answer with what you have."; isError = true; }
                else { workers++; output = await RunWorker(Str(call.Input, "subtopic"), $"worker-{workers}", ct); isError = output.StartsWith("Stopped:"); }
                results.Add(new ToolResultBlockParam(call.ID) { Content = output, IsError = isError });
            }
            history.Add(new UserMessageParam { Content = results });
        }
        if (turns > MaxTurnsLead) stoppedBy = "turn_limit";
        var leadTurns = Math.Min(turns, MaxTurnsLead);

        // Citations are checked in C#, not trusted. One repair turn: name the uncited sentences and ask for a rewrite.
        var uncited = UncitedSentences(answer);
        if (stoppedBy == "end_turn" && answer.Length > 0 && uncited.Count > 0 && _tokens < TokenBudget)
        {
            history.Add(new UserMessageParam { Content = RepairRequest(uncited) });
            var fix = await claude.SendAsync(new MessageCreateParams { Model = options.Model, MaxTokens = 1500, System = LeadPrompt, Tools = LeadTools, Messages = history.ToList() }, ct);
            inTok += fix.Usage.InputTokens; outTok += fix.Usage.OutputTokens; _tokens += fix.Usage.InputTokens + fix.Usage.OutputTokens;
            leadTurns++;
            var rewritten = TextOf(fix);
            if (fix.StopReason == StopReason.EndTurn && rewritten.Length > 0) { answer = rewritten; uncited = UncitedSentences(answer); }
        }
        _usage.Insert(0, new AgentUsage("lead", leadTurns, inTok, outTok));
        return new ResearchResult(answer, answer.Length > 0 && uncited.Count == 0, uncited, _usage, _tokens, stoppedBy);
    }

    /// <summary>One worker: its own conversation, only the two policy tools, its own turn limit.</summary>
    async Task<string> RunWorker(string subtopic, string name, CancellationToken ct)
    {
        // A NEW conversation: the worker sees only its subtopic, never the lead's question or other findings.
        var history = new List<MessageParam> { new UserMessageParam { Content = subtopic } };
        int turns = 0; long inTok = 0, outTok = 0;
        try
        {
            while (turns < MaxTurnsPerWorker)
            {
                if (_tokens >= TokenBudget)
                    return $"Stopped: token budget of {TokenBudget} tokens used up after {turns} worker turn(s) on \"{subtopic}\". No complete findings.";

                turns++;
                var res = await claude.SendAsync(new MessageCreateParams { Model = options.Model, MaxTokens = 1000, System = WorkerPrompt, Tools = WorkerTools, Messages = history.ToList() }, ct);
                inTok += res.Usage.InputTokens; outTok += res.Usage.OutputTokens; _tokens += res.Usage.InputTokens + res.Usage.OutputTokens;
                history.Add(new AssistantMessageParam { Content = res.Content.Select(b => new ContentBlockParam(b.Json)).ToList() });

                if (res.StopReason != StopReason.ToolUse)
                {
                    var text = TextOf(res);
                    if (res.StopReason == StopReason.EndTurn && text.Length > 0) return text;
                    return $"Stopped: the worker ended with {res.StopReason?.Raw() ?? "unknown"} before finishing \"{subtopic}\". Partial findings: {text}";
                }

                // One tool_result for every tool_use. Only the two policy tools exist for a worker.
                var results = new List<ContentBlockParam>();
                foreach (var block in res.Content)
                {
                    if (!block.TryPickToolUse(out var call)) continue;
                    string output; bool isError = false;
                    try
                    {
                        (output, isError) = call.Name switch
                        {
                            "search_policies" => (await SearchAsync(Str(call.Input, "query")), false),
                            "read_policy" => (await ReadAsync(Str(call.Input, "id")), false),
                            _ => ($"Unknown tool {call.Name}. Use search_policies or read_policy.", true),
                        };
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        output = $"The tool {call.Name} failed ({e.GetType().Name})."; isError = true;
                    }
                    results.Add(new ToolResultBlockParam(call.ID) { Content = output, IsError = isError });
                }
                history.Add(new UserMessageParam { Content = results });
            }
            return $"Stopped: worker turn limit of {MaxTurnsPerWorker} reached on \"{subtopic}\". No complete findings.";
        }
        finally
        {
            _usage.Add(new AgentUsage(name, turns, inTok, outTok));
        }
    }

    async Task<string> SearchAsync(string query)
    {
        var all = await db.Policies.ToListAsync();
        var hits = PolicySearch.Find(all, query, 5).Select(p => new { p.Id, p.Title });
        return JsonSerializer.Serialize(hits);
    }

    async Task<string> ReadAsync(string id) =>
        await db.Policies.FindAsync(id.Trim().ToUpperInvariant()) is { } p ? JsonSerializer.Serialize(new { p.Id, p.Title, p.Body }) : $"No policy with id {id}.";

    /// <summary>The repair message: the exact sentences without a policy id.</summary>
    public static string RepairRequest(IEnumerable<string> uncited) =>
        "These sentences have no policy id: " + string.Join(" | ", uncited.Select(s => $"\"{s}\"")) +
        ". Rewrite the whole answer under the same rules: end every sentence with the policy ids it relies on, or remove the sentence. Do not call research again.";

    /// <summary>Every sentence must cite at least one policy id like [POL-03]. An id written just after the full stop still counts for that sentence.</summary>
    public static List<string> UncitedSentences(string answer) =>
        Regex.Split(answer.Trim(), @"(?<=[.!?\]])\s+(?!\[)")
            .Select(s => s.Trim())
            .Where(s => s.Length > 3 && !Regex.IsMatch(s, @"\[POL-\d{2}(,\s*POL-\d{2})*\]"))
            .ToList();

    static string Str(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    static string TextOf(Message m) =>
        string.Join("\n", m.Content.Select(b => b.TryPickText(out var t) ? t.Text : null).Where(s => !string.IsNullOrWhiteSpace(s)));

    static ToolUnion Tool(string name, string description, object properties, params string[] required) => new Tool
    {
        Name = name,
        Description = description,
        InputSchema = new InputSchema
        {
            Properties = JsonSerializer.SerializeToElement(properties).EnumerateObject().ToDictionary(p => p.Name, p => p.Value),
            Required = required,
        },
    };
}
