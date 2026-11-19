using System.Text.Json;
using Anthropic.Models.Messages;
using Api.Services;

namespace Api.Documents;

/// <summary>Lab 6: turn a document into checked data, or send it to a person.</summary>
public class DocumentExtractor(IClaudeChat claude, ClaudeOptions options)
{
    public const int MaxAttempts = 2; // first try + one retry

    const string Prompt = """
        Extract the data from this Acme HR document. It is a medical certificate (MC), an expense claim, or something else (kind "other").
        Dates as yyyy-MM-dd. Numeric dates such as 03/10/2026 are written day first (dd/MM/yyyy), as is usual in Malaysia: 03/10/2026 is 2026-10-03.
        Amounts as numbers without currency symbols. Use null for the part that does not apply.
        Copy values exactly as written. Do not fix or invent numbers.
        """;

    /// <summary>The JSON schema Claude must follow (structured outputs).</summary>
    public static readonly Dictionary<string, JsonElement> Schema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "kind", "medical_certificate", "expense_claim" },
        properties = new
        {
            kind = new { type = "string", @enum = new[] { "medical_certificate", "expense_claim", "other" } },
            medical_certificate = new
            {
                type = new[] { "object", "null" },
                additionalProperties = false,
                required = new[] { "employee_name", "clinic", "date_from", "date_to", "days" },
                properties = new
                {
                    employee_name = new { type = "string" }, clinic = new { type = "string" },
                    date_from = new { type = "string" }, date_to = new { type = "string" }, days = new { type = "integer" },
                },
            },
            expense_claim = new
            {
                type = new[] { "object", "null" },
                additionalProperties = false,
                required = new[] { "claim_no", "vendor", "date", "currency", "subtotal", "tax", "total", "items" },
                properties = new
                {
                    claim_no = new { type = "string" }, vendor = new { type = "string" }, date = new { type = "string" },
                    currency = new { type = "string" }, subtotal = new { type = "number" }, tax = new { type = "number" }, total = new { type = "number" },
                    items = new
                    {
                        type = "array",
                        items = new { type = "object", additionalProperties = false, required = new[] { "description", "amount" }, properties = new { description = new { type = "string" }, amount = new { type = "number" } } },
                    },
                },
            },
        },
    }).EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

    /// <summary>The first message: the PDF as a document block (Claude reads its text and its pages as images), then the ask.</summary>
    public static UserMessageParam DocumentMessage(byte[] pdf) => new()
    {
        Content = new List<ContentBlockParam>
        {
            new DocumentBlockParam { Source = new Base64PdfSource { Data = Convert.ToBase64String(pdf) } },
            new TextBlockParam { Text = "Extract the data from this document." },
        },
    };

    /// <summary>
    /// The schema fixes the shape; <see cref="DocumentValidator"/> checks the meaning in C#. One retry with the exact
    /// problems, at most <see cref="MaxAttempts"/> calls. Anything not cleanly valid goes to a person as NeedsReview.
    /// </summary>
    public async Task<ExtractionResult> ExtractAsync(byte[] pdf, CancellationToken ct = default)
    {
        var history = new List<MessageParam> { DocumentMessage(pdf) };
        Extraction? data = null;
        List<string> problems = [];

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var res = await claude.SendAsync(new MessageCreateParams
            {
                Model = options.Model,
                MaxTokens = 1024,
                System = Prompt,
                Messages = history.ToList(), // a copy: each request keeps the history it was sent with
                OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = Schema } },
            }, ct);

            // Cut off (max_tokens), refused, or anything else unexpected: never trust or retry a partial answer.
            if (res.StopReason != StopReason.EndTurn)
                return new ExtractionResult(data, DocumentStatus.NeedsReview,
                    [$"Claude stopped with stop_reason {res.StopReason?.Raw() ?? "unknown"} instead of end_turn"], attempt);

            var text = string.Concat(res.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
            (data, problems) = ParseAndCheck(text);

            if (problems.Count == 0) return new ExtractionResult(data, DocumentStatus.Valid, [], attempt);
            if (data?.Kind == "other") return new ExtractionResult(data, DocumentStatus.NeedsReview, problems, attempt); // nothing to correct
            if (attempt == MaxAttempts) break;

            history.Add(new AssistantMessageParam { Content = text });
            history.Add(new UserMessageParam { Content = RetryMessage(problems) });
        }

        return new ExtractionResult(data, DocumentStatus.NeedsReview, problems, MaxAttempts);
    }

    /// <summary>Parse Claude's text into <see cref="Extraction"/> and run the business rules. Bad JSON is a problem, not a crash.</summary>
    static (Extraction? Data, List<string> Problems) ParseAndCheck(string text)
    {
        Extraction? data;
        try { data = JsonSerializer.Deserialize<Extraction>(text); }
        catch (JsonException e) { return (null, [$"the answer is not valid JSON for the schema: {e.Message}"]); }
        if (data is null) return (null, ["the answer is not valid JSON for the schema: it is empty or null"]);
        return (data, DocumentValidator.Check(data));
    }

    /// <summary>
    /// The retry ask: the exact problems, and a firm rule not to "fix" the document itself. A document that is wrong
    /// on paper (samples 08, 10, 17) must keep its values and fail again, so it reaches a person.
    /// </summary>
    static string RetryMessage(List<string> problems) =>
        "Your answer failed these checks:\n" +
        string.Join("\n", problems.Select(p => $"- {p}")) +
        "\n\nLook at the document again and answer again with the same JSON shape. " +
        "When you answer, correct only reading mistakes; if the document itself is wrong, keep its values as written. " +
        "Do not change a value just to make these checks pass.";
}
