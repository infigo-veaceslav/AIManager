using System.Text;
using System.Text.Json;
using AIManager.Api.Data;
using AIManager.Api.Domain;
using AIManager.Api.Services;
using AIManager.Api.Services.Ai;
using AIManager.Api.Services.Jira;
using AIManager.Api.Services.Settings;
using AIManager.Api.Services.Teams;
using Microsoft.EntityFrameworkCore;

namespace AIManager.Api.Jobs;

/// <summary>
/// Checks tickets that were Active or had time logged the previous working day for a proper
/// progress update (work done / remaining / estimate), then reports gaps to the operator.
/// </summary>
public class TaskUpdateTrackerJob
{
    private readonly AppDbContext _db;
    private readonly IJiraService _jira;
    private readonly IUpdateJudge _judge;
    private readonly ITeamsService _teams;
    private readonly ISettingsService _settings;
    private readonly ILogger<TaskUpdateTrackerJob> _log;

    public TaskUpdateTrackerJob(
        AppDbContext db, IJiraService jira, IUpdateJudge judge, ITeamsService teams,
        ISettingsService settings, ILogger<TaskUpdateTrackerJob> log)
    {
        _db = db;
        _jira = jira;
        _judge = judge;
        _teams = teams;
        _settings = settings;
        _log = log;
    }

    private sealed record TrackerConfig(string? ScopeJql, int LookbackDays = 2, int MaxIssues = 50);

    public async Task RunAllAsync(CancellationToken ct = default)
    {
        var ruleIds = await _db.ChaseRules
            .Where(r => r.Enabled && r.Type == ChaseRuleType.TaskUpdate)
            .Select(r => r.Id)
            .ToListAsync(ct);

        foreach (var id in ruleIds)
            await RunRuleAsync(id, ct);
    }

    public async Task RunRuleAsync(int ruleId, CancellationToken ct = default)
    {
        var rule = await _db.ChaseRules.Include(r => r.Team).Include(r => r.DestinationChannel)
            .FirstOrDefaultAsync(r => r.Id == ruleId, ct);
        if (rule is null || rule.Type != ChaseRuleType.TaskUpdate || rule.Team is null)
        {
            _log.LogWarning("TaskUpdate rule {RuleId} not found or wrong type.", ruleId);
            return;
        }

        var cfg = ParseConfig(rule.ConfigJson);
        var tz = rule.Team.Timezone;
        var date = WorkingDays.PreviousWorkingDay(tz, DateTimeOffset.UtcNow);
        var since = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, cfg.LookbackDays));

        var issues = await _jira.GetActiveOrWorkloggedIssuesAsync(date, cfg.ScopeJql, ct);

        var truncated = false;
        if (issues.Count > cfg.MaxIssues)
        {
            _log.LogWarning("TaskUpdate rule {RuleId}: {Total} issues found, capping at {Max}.",
                ruleId, issues.Count, cfg.MaxIssues);
            issues = issues.Take(cfg.MaxIssues).ToList();
            truncated = true;
        }

        // Clear prior findings for this target date so re-runs are idempotent.
        var stale = _db.TaskUpdateFindings.Where(f => f.TargetDate == date);
        _db.TaskUpdateFindings.RemoveRange(stale);
        await _db.SaveChangesAsync(ct);

        var findings = new List<TaskUpdateFinding>();
        foreach (var issue in issues)
        {
            var comments = await _jira.GetCommentsAsync(issue.Key, since, ct);
            var j = await _judge.JudgeAsync(issue.Key, issue.Summary, comments, ct);

            findings.Add(new TaskUpdateFinding
            {
                TargetDate = date,
                IssueKey = issue.Key,
                Summary = issue.Summary,
                Assignee = issue.AssigneeName,
                AssigneeEmail = issue.AssigneeEmail,
                Status = issue.Status,
                HasWorkDone = j.HasWorkDone,
                HasRemaining = j.HasRemaining,
                HasEstimate = j.HasEstimate,
                Missing = j.Missing.Count > 0 ? string.Join(", ", j.Missing) : null,
                Verdict = j.Verdict,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        _db.TaskUpdateFindings.AddRange(findings);
        await _db.SaveChangesAsync(ct);

        var report = BuildReport(date, findings, truncated);

        // A destination channel wins; otherwise DM the operator (test override, then configured recipient).
        bool sent;
        string deliveredTo;
        ChaseOutcome outcome;
        if (rule.DestinationChannel is not null)
        {
            sent = await _teams.SendChannelAsync(rule.DestinationChannel.TeamId, rule.DestinationChannel.ChannelId, report, ct);
            deliveredTo = $"#{rule.DestinationChannel.Name}";
            outcome = sent ? ChaseOutcome.Sent : ChaseOutcome.Failed;
        }
        else
        {
            var reportRecipient = (await _settings.GetEffectiveAsync(ct)).Teams.ReportRecipient;
            deliveredTo = string.IsNullOrWhiteSpace(rule.TestRecipientOverride) ? reportRecipient : rule.TestRecipientOverride!;
            if (string.IsNullOrWhiteSpace(deliveredTo))
            {
                _log.LogWarning("No report recipient/channel configured for TaskUpdate rule {RuleId}.", ruleId);
                sent = false;
                outcome = ChaseOutcome.Skipped;
            }
            else
            {
                sent = await _teams.SendDmAsync(deliveredTo, report, ct);
                outcome = sent ? ChaseOutcome.Sent : ChaseOutcome.Failed;
            }
        }

        _db.ChaseEvents.Add(new ChaseEvent
        {
            RuleId = rule.Id,
            MemberId = null,
            TargetDate = date,
            Reason = $"Task-update report: {findings.Count(f => !f.IsComplete)}/{findings.Count} tickets missing a proper update",
            DetailJson = JsonSerializer.Serialize(new
            {
                total = findings.Count,
                incomplete = findings.Count(f => !f.IsComplete),
                truncated
            }),
            Outcome = outcome,
            MessageText = report,
            DeliveredTo = deliveredTo,
            CreatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    private static TrackerConfig ParseConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new TrackerConfig(null);
        try
        {
            return JsonSerializer.Deserialize<TrackerConfig>(json,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new TrackerConfig(null);
        }
        catch (JsonException)
        {
            return new TrackerConfig(null);
        }
    }

    private static string BuildReport(DateOnly date, List<TaskUpdateFinding> findings, bool truncated)
    {
        var incomplete = findings.Where(f => !f.IsComplete).ToList();
        var sb = new StringBuilder();
        sb.Append($"<b>Task-update check for {date:yyyy-MM-dd}</b><br>");
        sb.Append($"{findings.Count} ticket(s) reviewed, <b>{incomplete.Count}</b> missing a proper update.<br><br>");

        if (incomplete.Count == 0)
        {
            sb.Append("✅ All reviewed tickets have a proper update. Nice.");
            return sb.ToString();
        }

        foreach (var group in incomplete
                     .GroupBy(f => f.Assignee ?? "Unassigned")
                     .OrderBy(g => g.Key))
        {
            sb.Append($"<b>{group.Key}</b><br>");
            foreach (var f in group)
            {
                var missing = string.IsNullOrEmpty(f.Missing) ? "update" : f.Missing;
                sb.Append($"• <a href=\"https://infigosoftware.atlassian.net/browse/{f.IssueKey}\">{f.IssueKey}</a> " +
                          $"({f.Status}) — missing: {missing}<br>");
            }
            sb.Append("<br>");
        }

        if (truncated)
            sb.Append("<i>Note: issue list was capped; some tickets were not reviewed.</i>");

        return sb.ToString();
    }
}
