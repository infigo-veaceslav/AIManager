using System.Globalization;
using System.Text.Json;
using AIManager.Api.Data;
using AIManager.Api.Domain;
using AIManager.Api.Services;
using AIManager.Api.Services.Jira;
using AIManager.Api.Services.Teams;
using Microsoft.EntityFrameworkCore;

namespace AIManager.Api.Jobs;

/// <summary>Chases team members who logged fewer than the threshold hours the previous working day.</summary>
public class TimeLogChaserJob
{
    private const string DefaultTemplate =
        "Hi {firstName} 👋<br><br>Quick reminder to log your time in Jira for <b>{date}</b> — " +
        "I'm currently seeing only <b>{hours}h</b> logged. Please get your time in by <b>10:00</b>.<br><br>Thanks!";

    private readonly AppDbContext _db;
    private readonly IJiraService _jira;
    private readonly ITeamsService _teams;
    private readonly ILogger<TimeLogChaserJob> _log;

    public TimeLogChaserJob(AppDbContext db, IJiraService jira, ITeamsService teams, ILogger<TimeLogChaserJob> log)
    {
        _db = db;
        _jira = jira;
        _teams = teams;
        _log = log;
    }

    /// <summary>Hangfire entry point: run every enabled TimeLog rule.</summary>
    public async Task RunAllAsync(CancellationToken ct = default)
    {
        var ruleIds = await _db.ChaseRules
            .Where(r => r.Enabled && r.Type == ChaseRuleType.TimeLog)
            .Select(r => r.Id)
            .ToListAsync(ct);

        foreach (var id in ruleIds)
            await RunRuleAsync(id, ct);
    }

    /// <summary>Run a single TimeLog rule (also used by the "run now" endpoint).</summary>
    public async Task RunRuleAsync(int ruleId, CancellationToken ct = default)
    {
        var rule = await _db.ChaseRules
            .Include(r => r.Team!).ThenInclude(t => t.Members)
            .FirstOrDefaultAsync(r => r.Id == ruleId, ct);

        if (rule is null || rule.Type != ChaseRuleType.TimeLog || rule.Team is null)
        {
            _log.LogWarning("TimeLog rule {RuleId} not found or wrong type.", ruleId);
            return;
        }

        var tz = rule.Team.Timezone;
        var date = WorkingDays.PreviousWorkingDay(tz, DateTimeOffset.UtcNow);
        var threshold = rule.ThresholdHours ?? 5.0;
        var members = rule.Team.Members.Where(m => m.Active).ToList();

        await EnsureAccountIdsAsync(members, ct);
        var accountIds = members.Where(m => m.JiraAccountId != null).Select(m => m.JiraAccountId!).ToList();

        var hoursByEmail = await _jira.GetWorklogHoursByEmailAsync(date, accountIds, tz, ct);

        _log.LogInformation("TimeLog rule {RuleId}: checking {Count} members for {Date} (threshold {Threshold}h).",
            ruleId, members.Count, date, threshold);

        foreach (var member in members)
        {
            var hours = hoursByEmail.TryGetValue(member.Email, out var h) ? h : 0.0;
            if (hours >= threshold) continue;

            // Idempotency: don't re-send if we already delivered for this member+date+rule.
            var already = await _db.ChaseEvents.AnyAsync(
                e => e.RuleId == rule.Id && e.MemberId == member.Id &&
                     e.TargetDate == date && e.Outcome == ChaseOutcome.Sent, ct);
            if (already)
            {
                _log.LogInformation("Already chased {Member} for {Date}; skipping.", member.Email, date);
                continue;
            }

            var message = RenderTemplate(rule.MessageTemplate ?? DefaultTemplate, member, date, hours);
            var recipient = string.IsNullOrWhiteSpace(rule.TestRecipientOverride)
                ? member.Email
                : rule.TestRecipientOverride!;

            var sent = await _teams.SendDmAsync(recipient, message, ct);

            _db.ChaseEvents.Add(new ChaseEvent
            {
                RuleId = rule.Id,
                MemberId = member.Id,
                TargetDate = date,
                Reason = $"Logged {hours:0.##}h (< {threshold:0.##}h) on {date:yyyy-MM-dd}",
                DetailJson = JsonSerializer.Serialize(new { hours, threshold, isTest = recipient != member.Email }),
                Outcome = sent ? ChaseOutcome.Sent : ChaseOutcome.Failed,
                MessageText = message,
                DeliveredTo = recipient,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task EnsureAccountIdsAsync(List<TeamMember> members, CancellationToken ct)
    {
        var changed = false;
        foreach (var member in members.Where(m => string.IsNullOrEmpty(m.JiraAccountId)))
        {
            var id = await _jira.ResolveAccountIdAsync(member.Email, ct);
            if (!string.IsNullOrEmpty(id))
            {
                member.JiraAccountId = id;
                changed = true;
            }
            else
            {
                _log.LogWarning("Could not resolve Jira accountId for {Email}.", member.Email);
            }
        }
        if (changed) await _db.SaveChangesAsync(ct);
    }

    private static string RenderTemplate(string template, TeamMember member, DateOnly date, double hours)
    {
        var firstName = member.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                        ?? member.DisplayName;
        return template
            .Replace("{name}", member.DisplayName)
            .Replace("{firstName}", firstName)
            .Replace("{date}", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Replace("{hours}", hours.ToString("0.##", CultureInfo.InvariantCulture));
    }
}
