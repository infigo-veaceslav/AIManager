using System.Net;
using System.Text;
using System.Text.Json;
using AIManager.Api.Data;
using AIManager.Api.Domain;
using AIManager.Api.Services;
using AIManager.Api.Services.Jira;
using AIManager.Api.Services.Teams;
using Microsoft.EntityFrameworkCore;

namespace AIManager.Api.Jobs;

/// <summary>
/// Posts a support-ticket digest to a channel, grouped by the responsible person and @mentioning them.
/// Assignee-statuses group under the assignee (unassigned → operator); reporter-statuses under the reporter.
/// </summary>
public class SupportDigestJob
{
    private readonly AppDbContext _db;
    private readonly IJiraService _jira;
    private readonly ITeamsService _teams;
    private readonly ILogger<SupportDigestJob> _log;

    private const string BrowseBase = "https://infigosoftware.atlassian.net/browse/";

    public SupportDigestJob(AppDbContext db, IJiraService jira, ITeamsService teams, ILogger<SupportDigestJob> log)
    {
        _db = db;
        _jira = jira;
        _teams = teams;
        _log = log;
    }

    private sealed record DigestConfig(
        string ProjectKey,
        string[] AssigneeStatuses,
        string[] ReporterStatuses,
        string[] ReporterFilter,
        string[] AssigneeFilter,
        string OperatorEmail,
        string OperatorName,
        int MaxPerPerson,
        bool Mention);

    public async Task RunAllAsync(CancellationToken ct = default)
    {
        var ruleIds = await _db.ChaseRules
            .Where(r => r.Enabled && r.Type == ChaseRuleType.SupportDigest)
            .Select(r => r.Id).ToListAsync(ct);
        foreach (var id in ruleIds)
            await RunRuleAsync(id, false, ct);
    }

    public async Task RunRuleAsync(int ruleId, bool manual, CancellationToken ct = default)
    {
        var rule = await _db.ChaseRules.Include(r => r.Team).Include(r => r.DestinationChannel)
            .FirstOrDefaultAsync(r => r.Id == ruleId, ct);
        if (rule is null || rule.Type != ChaseRuleType.SupportDigest || rule.Team is null)
        {
            _log.LogWarning("SupportDigest rule {RuleId} not found or wrong type.", ruleId);
            return;
        }
        if (rule.DestinationChannel is null)
        {
            _log.LogWarning("SupportDigest rule {RuleId} has no destination channel set — skipping.", ruleId);
            return;
        }

        var trigger = manual ? ChaseTrigger.Manual : ChaseTrigger.Scheduled;
        var cfg = ParseConfig(rule.ConfigJson);
        var date = WorkingDays.TodayIn(rule.Team.Timezone, DateTimeOffset.UtcNow);
        var statuses = cfg.AssigneeStatuses.Concat(cfg.ReporterStatuses).Distinct().ToArray();

        List<SupportIssue> issues;
        try
        {
            // Fetch by status; reporter/assignee filtering is applied per bucket in BuildDigest.
            issues = await _jira.GetSupportIssuesAsync(cfg.ProjectKey, statuses, Array.Empty<string>(), 0, ct);
        }
        catch (JiraUnavailableException ex)
        {
            _log.LogError(ex, "SupportDigest rule {RuleId}: Jira unavailable; skipping run.", rule.Id);
            _db.ChaseEvents.Add(new ChaseEvent
            {
                RuleId = rule.Id, MemberId = null, TargetDate = date,
                Reason = "Skipped: Jira throttled/unavailable — digest not posted",
                DetailJson = JsonSerializer.Serialize(new { error = ex.Message }),
                Outcome = ChaseOutcome.Skipped, Trigger = trigger, CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
            return;
        }

        var (message, people, groupCount, ticketCount) = BuildDigest(issues, cfg);

        bool sent;
        var channel = rule.DestinationChannel;
        if (cfg.Mention && people.Count > 0)
            sent = await _teams.SendChannelWithMentionsAsync(channel.TeamId, channel.ChannelId, message, people, ct);
        else
            sent = await _teams.SendChannelAsync(channel.TeamId, channel.ChannelId, message, ct);

        _db.ChaseEvents.Add(new ChaseEvent
        {
            RuleId = rule.Id,
            MemberId = null,
            TargetDate = date,
            Reason = $"Support digest: {ticketCount} ticket(s) across {groupCount} people",
            DetailJson = JsonSerializer.Serialize(new { tickets = ticketCount, people = groupCount, project = cfg.ProjectKey }),
            Outcome = sent ? ChaseOutcome.Sent : ChaseOutcome.Failed,
            Trigger = trigger,
            MessageText = message,
            DeliveredTo = $"#{channel.Name}",
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    private static (string message, List<MentionTarget> people, int groupCount, int ticketCount) BuildDigest(List<SupportIssue> issues, DigestConfig cfg)
    {
        var reporterStatuses = new HashSet<string>(cfg.ReporterStatuses, StringComparer.OrdinalIgnoreCase);
        var reporterFilter = new HashSet<string>(cfg.ReporterFilter, StringComparer.OrdinalIgnoreCase);
        var assigneeFilter = new HashSet<string>(cfg.AssigneeFilter, StringComparer.OrdinalIgnoreCase);

        // Group by the responsible person, applying per-bucket coverage filters.
        var groups = new Dictionary<string, (string Display, List<SupportIssue> Issues)>(StringComparer.OrdinalIgnoreCase);
        foreach (var issue in issues)
        {
            // Both filters must pass (AND). An empty filter means "no restriction" on that dimension.
            var assigneeOk = assigneeFilter.Count == 0 ||
                             (issue.AssigneeEmail is not null && assigneeFilter.Contains(issue.AssigneeEmail));
            var reporterOk = reporterFilter.Count == 0 ||
                             (issue.ReporterEmail is not null && reporterFilter.Contains(issue.ReporterEmail));
            if (!assigneeOk || !reporterOk) continue;

            // Bucket the ticket under the person responsible for its status.
            string email, display;
            if (reporterStatuses.Contains(issue.Status))
            {
                email = issue.ReporterEmail ?? cfg.OperatorEmail;
                display = issue.ReporterEmail is null ? cfg.OperatorName : (issue.ReporterName ?? issue.ReporterEmail);
            }
            else if (issue.AssigneeEmail is null)
            {
                // Unassigned assignee-status ticket → the operator ("for me").
                email = cfg.OperatorEmail;
                display = cfg.OperatorName;
            }
            else
            {
                email = issue.AssigneeEmail;
                display = issue.AssigneeName ?? issue.AssigneeEmail;
            }

            if (!groups.TryGetValue(email, out var g))
            {
                g = (display, new List<SupportIssue>());
                groups[email] = g;
            }
            g.Issues.Add(issue);
        }

        var ticketCount = groups.Values.Sum(g => g.Issues.Count);
        if (groups.Count == 0)
            return ("<b>Support tickets</b><br>Nothing needs attention right now ✅", new List<MentionTarget>(), 0, 0);

        var people = new List<MentionTarget>();
        var sb = new StringBuilder();
        sb.Append("<b>Support tickets needing attention</b><br>");
        sb.Append($"{ticketCount} ticket(s) across {groups.Count} people.<br><br>");

        var idx = 0;
        foreach (var kv in groups.OrderBy(k => k.Value.Display, StringComparer.OrdinalIgnoreCase))
        {
            var email = kv.Key;
            var display = kv.Value.Display;
            var list = kv.Value.Issues;

            if (cfg.Mention)
            {
                people.Add(new MentionTarget(idx, email, display));
                sb.Append($"<at id=\"{idx}\">{WebUtility.HtmlEncode(display)}</at> — {list.Count} ticket(s)<br>");
            }
            else
            {
                sb.Append($"<b>{WebUtility.HtmlEncode(display)}</b> — {list.Count} ticket(s)<br>");
            }

            var shown = cfg.MaxPerPerson > 0 ? list.Take(cfg.MaxPerPerson).ToList() : list;
            foreach (var it in shown)
                sb.Append($"• <a href=\"{BrowseBase}{it.Key}\">{it.Key}</a> — " +
                          $"{WebUtility.HtmlEncode(it.Summary)} <i>({WebUtility.HtmlEncode(it.Status)})</i><br>");

            if (cfg.MaxPerPerson > 0 && list.Count > cfg.MaxPerPerson)
                sb.Append($"…and {list.Count - cfg.MaxPerPerson} more<br>");

            sb.Append("<br>");
            idx++;
        }

        return (sb.ToString(), people, groups.Count, ticketCount);
    }

    private static DigestConfig ParseConfig(string? json)
    {
        var defaults = new DigestConfig(
            "SUP",
            new[] { "Triage", "Assigned", "In Progress", "Needs assignee attention", "Support Backlog" },
            new[] { "Customer Feedback", "Needs reporter attention" },
            Array.Empty<string>(),
            Array.Empty<string>(),
            "veaceslav.andreev@infigo.net",
            "Veaceslav Andreev",
            0,
            false);

        if (string.IsNullOrWhiteSpace(json)) return defaults;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            return new DigestConfig(
                GetString(r, "projectKey") ?? defaults.ProjectKey,
                GetArray(r, "assigneeStatuses") ?? defaults.AssigneeStatuses,
                GetArray(r, "reporterStatuses") ?? defaults.ReporterStatuses,
                GetArray(r, "reporterFilter") ?? defaults.ReporterFilter,
                GetArray(r, "assigneeFilter") ?? defaults.AssigneeFilter,
                GetString(r, "operatorEmail") ?? defaults.OperatorEmail,
                GetString(r, "operatorName") ?? defaults.OperatorName,
                r.TryGetProperty("maxPerPerson", out var m) && m.TryGetInt32(out var mv) ? mv : defaults.MaxPerPerson,
                GetBool(r, "mention") ?? defaults.Mention);
        }
        catch (JsonException)
        {
            return defaults;
        }
    }

    private static bool? GetBool(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : null;

    private static string? GetString(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string[]? GetArray(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
            : null;
}
