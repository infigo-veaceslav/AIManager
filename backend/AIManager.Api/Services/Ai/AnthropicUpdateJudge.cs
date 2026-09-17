using System.Text;
using System.Text.Json;
using AIManager.Api.Services.Jira;
using AIManager.Api.Services.Settings;

namespace AIManager.Api.Services.Ai;

public class AnthropicUpdateJudge : IUpdateJudge
{
    private readonly HttpClient _http;
    private readonly ISettingsService _settings;
    private readonly ILogger<AnthropicUpdateJudge> _log;

    private const string SystemPrompt =
        "You review Jira ticket progress updates. Given a ticket and its recent comments, decide whether " +
        "the comments together contain a proper progress update with these three parts:\n" +
        "1. work_done: what has actually been done so far;\n" +
        "2. remaining: what work still remains;\n" +
        "3. estimate: an estimate (time or effort) for the remaining part.\n\n" +
        "Respond with ONLY a JSON object, no prose, of the exact shape:\n" +
        "{\"hasWorkDone\":bool,\"hasRemaining\":bool,\"hasEstimate\":bool,\"missing\":[string],\"verdict\":string}\n" +
        "\"missing\" lists which of work_done/remaining/estimate are absent. \"verdict\" is one short sentence. " +
        "Be strict: a vague or purely status-change comment does not count as an update.";

    public AnthropicUpdateJudge(HttpClient http, ISettingsService settings, ILogger<AnthropicUpdateJudge> log)
    {
        _http = http;
        _settings = settings;
        _log = log;
    }

    public async Task<UpdateJudgement> JudgeAsync(
        string issueKey, string summary, IReadOnlyList<IssueComment> comments, CancellationToken ct = default)
    {
        var cfg = (await _settings.GetEffectiveAsync(ct)).Anthropic;
        if (string.IsNullOrWhiteSpace(cfg.ApiKey))
        {
            _log.LogWarning("Anthropic API key not configured — {IssueKey} not evaluated.", issueKey);
            return new UpdateJudgement(false, false, false,
                new[] { "work_done", "remaining", "estimate" }, "Not evaluated (no API key).");
        }

        var userContent = BuildUserContent(issueKey, summary, comments);
        var requestBody = JsonSerializer.Serialize(new
        {
            model = cfg.Model,
            max_tokens = cfg.MaxTokens,
            system = SystemPrompt,
            messages = new[] { new { role = "user", content = userContent } }
        });

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{cfg.BaseUrl.TrimEnd('/')}/v1/messages");
            req.Headers.Add("x-api-key", cfg.ApiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
            req.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("Anthropic {IssueKey} -> {Status}: {Body}", issueKey, (int)resp.StatusCode, raw);
                return Unevaluated("API error.");
            }

            var text = ExtractText(raw);
            return ParseJudgement(text) ?? Unevaluated("Unparseable model response.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Anthropic judge failed for {IssueKey}", issueKey);
            return Unevaluated("Judge threw.");
        }
    }

    private static string BuildUserContent(string issueKey, string summary, IReadOnlyList<IssueComment> comments)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Ticket: {issueKey} — {summary}");
        sb.AppendLine();
        if (comments.Count == 0)
        {
            sb.AppendLine("(No recent comments.)");
        }
        else
        {
            sb.AppendLine("Recent comments (newest first):");
            foreach (var c in comments)
            {
                sb.AppendLine($"--- {c.Created:yyyy-MM-dd HH:mm} by {c.AuthorName ?? "unknown"} ---");
                sb.AppendLine(c.Text);
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    private static string ExtractText(string rawResponse)
    {
        using var doc = JsonDocument.Parse(rawResponse);
        if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var block in content.EnumerateArray())
                if (block.TryGetProperty("type", out var t) && t.GetString() == "text" &&
                    block.TryGetProperty("text", out var txt))
                    sb.Append(txt.GetString());
            return sb.ToString();
        }
        return "";
    }

    private static UpdateJudgement? ParseJudgement(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            var r = doc.RootElement;
            var missing = new List<string>();
            if (r.TryGetProperty("missing", out var m) && m.ValueKind == JsonValueKind.Array)
                missing.AddRange(m.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0));

            return new UpdateJudgement(
                r.TryGetProperty("hasWorkDone", out var wd) && wd.GetBoolean(),
                r.TryGetProperty("hasRemaining", out var rm) && rm.GetBoolean(),
                r.TryGetProperty("hasEstimate", out var es) && es.GetBoolean(),
                missing,
                r.TryGetProperty("verdict", out var v) ? v.GetString() ?? "" : "");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static UpdateJudgement Unevaluated(string reason) =>
        new(false, false, false, new[] { "work_done", "remaining", "estimate" }, reason);
}
