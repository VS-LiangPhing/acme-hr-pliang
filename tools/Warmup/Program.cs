// Module 2 warm-up: the smallest complete tool-use loop, with one tool.
// Run:  dotnet run --project tools/Warmup -- "How many leave days do I have?"
// Reads ANTHROPIC_API_KEY from .env at the repo root. Model from CLAUDE_MODEL, default claude-sonnet-5-5.
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

var question = args.Length > 0 ? string.Join(' ', args) : "How many leave days do I have left?";
var model = Environment.GetEnvironmentVariable("CLAUDE_MODEL") ?? "claude-sonnet-5-5";
const int MaxTurns = 5;

// Read ANTHROPIC_API_KEY from the repo's .env, like the API does. Keep it out of your shell:
// if it is exported where you run `claude`, Claude Code bills that key instead of your Claude plan.
LoadDotEnv();
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
{
    Console.WriteLine("Put ANTHROPIC_API_KEY in .env at the repo root (never in code).");
    return;
}
AnthropicClient client = new(); // reads ANTHROPIC_API_KEY

List<ToolUnion> tools =
[
    Def("get_leave_balance", "Get the annual leave balance of the current employee, in working days.", new { }),
    Def("get_public_holidays",
        "Get the 2026 public holidays for one Malaysian state. Use the state code: SEL (Selangor), KUL (Kuala Lumpur), PNG (Penang).",
        new { state = new { type = "string", description = "State code, for example KUL for Kuala Lumpur." } },
        "state"),
];

var history = new List<MessageParam> { new UserMessageParam { Content = question } };

for (var turn = 1; turn <= MaxTurns; turn++)
{
    var res = await client.Messages.Create(new MessageCreateParams
    {
        Model = model,
        MaxTokens = 1024,
        System = "You are the Acme HR helpdesk. Use the tools; never guess numbers. Today is " + DateTime.Today.ToString("yyyy-MM-dd") + ".",
        Tools = tools,
        Messages = history,
    });
    Console.WriteLine($"[turn {turn}] stop_reason={res.StopReason?.Raw()} in={res.Usage.InputTokens} out={res.Usage.OutputTokens}");

    // The API is stateless: keep Claude's turn in the history and resend it.
    history.Add(new AssistantMessageParam { Content = res.Content.Select(b => new ContentBlockParam(b.Json)).ToList() });

    if (res.StopReason != StopReason.ToolUse)
    {
        foreach (var b in res.Content) if (b.TryPickText(out var t)) Console.WriteLine("\n" + t.Text);
        return;
    }

    // Claude asked for tools. Our code runs them and answers every tool_use id.
    var results = new List<ContentBlockParam>();
    foreach (var b in res.Content)
    {
        if (!b.TryPickToolUse(out var call)) continue;
        var (output, isError) = RunTool(call.Name, call.Input);
        Console.WriteLine($"  tool_use {call.Name}({JsonSerializer.Serialize(call.Input)}) -> {output}");
        results.Add(new ToolResultBlockParam(call.ID) { Content = output, IsError = isError });
    }
    history.Add(new UserMessageParam { Content = results });
}
Console.WriteLine("Stopped: turn limit reached.");

static (string Output, bool IsError) RunTool(string name, IReadOnlyDictionary<string, JsonElement> input) => name switch
{
    "get_leave_balance" => ("""{"entitlement":16,"used":5.5,"pending":2,"available":8.5}""", false),
    "get_public_holidays" => PublicHolidays(input),
    _ => ($"Unknown tool {name}.", true),
};

// Fake training data: a short sample of 2026 holidays, not an official calendar.
static (string Output, bool IsError) PublicHolidays(IReadOnlyDictionary<string, JsonElement> input)
{
    var state = input.TryGetValue("state", out var s) && s.ValueKind == JsonValueKind.String
        ? s.GetString()!.Trim().ToUpperInvariant() : "";
    (string Date, string Name)[] national =
    [
        ("2026-01-01", "New Year's Day"),
        ("2026-02-17", "Chinese New Year"),
        ("2026-02-18", "Chinese New Year (second day)"),
        ("2026-03-21", "Hari Raya Aidilfitri"),
        ("2026-05-01", "Labour Day"),
        ("2026-08-31", "National Day"),
        ("2026-09-16", "Malaysia Day"),
        ("2026-12-25", "Christmas Day"),
    ];
    (string Date, string Name)[]? local = state switch
    {
        "SEL" => [("2026-02-01", "Thaipusam"), ("2026-12-11", "Sultan of Selangor's Birthday")],
        "KUL" => [("2026-02-01", "Federal Territory Day"), ("2026-02-01", "Thaipusam")],
        "PNG" => [("2026-02-01", "Thaipusam"), ("2026-07-07", "George Town World Heritage City Day")],
        _ => null,
    };
    if (local is null)
        return ($"No holiday data for state '{state}'. Use SEL, KUL or PNG.", true);
    var list = national.Concat(local).OrderBy(h => h.Date).Select(h => new { date = h.Date, name = h.Name });
    return (JsonSerializer.Serialize(new { state, year = 2026, holidays = list }), false);
}

static ToolUnion Def(string name, string description, object properties, params string[] required) => new Tool
{
    Name = name,
    Description = description,
    InputSchema = new InputSchema
    {
        Properties = JsonSerializer.SerializeToElement(properties).EnumerateObject().ToDictionary(p => p.Name, p => p.Value),
        Required = required,
    },
};

static void LoadDotEnv()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
    {
        var file = Path.Combine(dir.FullName, ".env");
        if (!File.Exists(file)) continue;
        foreach (var line in File.ReadAllLines(file))
        {
            var i = line.IndexOf('=');
            if (line.TrimStart().StartsWith('#') || i <= 0) continue;
            var key = line[..i].Trim();
            if (Environment.GetEnvironmentVariable(key) is null) Environment.SetEnvironmentVariable(key, line[(i + 1)..].Trim());
        }
        return;
    }
}
