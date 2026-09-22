using System.Globalization;
using System.Text;
using System.Text.Json;
using AIManager.Api.Data;
using AIManager.Api.Domain;
using AIManager.Api.Services;
using AIManager.Api.Services.Compliance;
using AIManager.Api.Services.Jira;
using AIManager.Api.Services.Teams;
using Microsoft.EntityFrameworkCore;

namespace AIManager.Api.Jobs;

/// <summary>Chases team members who logged fewer than the threshold hours the previous working day.</summary>
public class TimeLogChaserJob
{
    // Sent before ~noon (e.g. the 09:45 run) — gentle, pre-deadline.
    private const string MorningTemplate =
        "Hi {firstName} 👋<br><br>Quick reminder to log your time in Jira for <b>{date}</b> — " +
        "I'm currently seeing only <b>{hours}h</b>. Please get it in by <b>10:00</b>.<br><br>Thanks!";

    // Sent in the afternoon (e.g. the 14:00 run) — firmer, past the deadline.
    private const string AfternoonTemplate =
        "Hi {firstName} 👋<br><br>It's past the <b>10:00</b> deadline and I still see only <b>{hours}h</b> " +
        "logged for <b>{date}</b>. Please log your time now.<br><br>Thanks!";

    private readonly AppDbContext _db;
    private readonly IJiraService _jira;
    private readonly ITeamsService _teams;
    private readonly IComplianceSnapshotStore _snapshots;
    private readonly ILogger<TimeLogChaserJob> _log;

    public TimeLogChaserJob(AppDbContext db, IJiraService jira, ITeamsService teams,
        IComplianceSnapshotStore snapshots, ILogger<TimeLogChaserJob> log)
    {
        _db = db;
        _jira = jira;
        _teams = teams;
        _snapshots = snapshots;
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
            .Include(r => r.DestinationChannel)
            .FirstOrDefaultAsync(r => r.Id == ruleId, ct);

        if (rule is null || rule.Type != ChaseRuleType.TimeLog || rule.Team is null)
        {
            _log.LogWarning("TimeLog rule {RuleId} not found or wrong type.", ruleId);
            return;
        }

        var tz = rule.Team.Timezone;
        var date = WorkingDays.PreviousWorkingDay(tz, rule.Team.WorkingDays, DateTimeOffset.UtcNow);
        var threshold = rule.ThresholdHours ?? 5.0;
        var members = rule.Team.Members.Where(m => m.Active).ToList();

        Dictionary<string, double> hoursByEmail;
        try
        {
            await EnsureAccountIdsAsync(members, ct);
            var accountIds = members.Where(m => m.JiraAccountId != null).Select(m => m.JiraAccountId!).ToList();
            hoursByEmail = await _jira.GetWorklogHoursByEmailAsync(date, accountIds, tz, ct);
        }
        catch (JiraUnavailableException ex)
        {
            // Fail safe: never chase on incomplete data — skip the run and try again next schedule.
            _log.LogError(ex, "TimeLog rule {RuleId}: Jira unavailable; skipping run (no reminders sent).", rule.Id);
            _db.ChaseEvents.Add(new ChaseEvent
            {
                RuleId = rule.Id,
                MemberId = null,
                TargetDate = date,
                Reason = "Skipped: Jira throttled/unavailable — no reminders sent",
                DetailJson = JsonSerializer.Serialize(new { error = ex.Message }),
                Outcome = ChaseOutcome.Skipped,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
            return;
        }

        // Refresh the compliance snapshot so the dashboard shows fresh data without a manual resync.
        try
        {
            var hoursById = members.ToDictionary(m => m.Id, m => hoursByEmail.TryGetValue(m.Email, out var h) ? h : 0.0);
            await _snapshots.UpsertAsync(rule.Team.Id, date, hoursById, DateTime.UtcNow, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "TimeLog rule {RuleId}: compliance snapshot refresh failed (non-fatal).", rule.Id);
        }

        _log.LogInformation("TimeLog rule {RuleId}: checking {Count} members for {Date} (threshold {Threshold}h).",
            ruleId, members.Count, date, threshold);

        var under = members
            .Select(m => (member: m, hours: hoursByEmail.TryGetValue(m.Email, out var h) ? h : 0.0))
            .Where(x => x.hours < threshold)
            .OrderBy(x => x.hours)
            .ToList();

        var wantsDm = rule.DeliveryMode is DeliveryMode.PerPersonDm or DeliveryMode.Both;
        var wantsChannel = rule.DeliveryMode is DeliveryMode.ChannelSummary or DeliveryMode.Both;

        if (wantsDm)
        {
            var (slotStartUtc, defaultTemplate) = ResolveSlot(tz);
            var template = string.IsNullOrWhiteSpace(rule.MessageTemplate) ? defaultTemplate : rule.MessageTemplate!;

            foreach (var (member, hours) in under)
            {
                // Skip only if already chased in the current half-day, so the 14:00 run still nudges
                // people already messaged at 09:45.
                var already = await _db.ChaseEvents.AnyAsync(
                    e => e.RuleId == rule.Id && e.MemberId == member.Id &&
                         e.TargetDate == date && e.Outcome == ChaseOutcome.Sent &&
                         e.CreatedAtUtc >= slotStartUtc, ct);
                if (already)
                {
                    _log.LogInformation("Already chased {Member} for {Date} this slot; skipping.", member.Email, date);
                    continue;
                }

                var message = RenderTemplate(template, member, date, hours);
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
        }

        if (wantsChannel && under.Count > 0)
        {
            if (rule.DestinationChannel is null)
            {
                _log.LogWarning("Rule {RuleId} wants a channel summary but has no destination channel set.", rule.Id);
            }
            else
            {
                var summary = BuildChannelSummary(date, threshold, under);
                var sent = await _teams.SendChannelAsync(
                    rule.DestinationChannel.TeamId, rule.DestinationChannel.ChannelId, summary, ct);

                _db.ChaseEvents.Add(new ChaseEvent
                {
                    RuleId = rule.Id,
                    MemberId = null,
                    TargetDate = date,
                    Reason = $"Team summary: {under.Count} under {threshold:0.##}h on {date:yyyy-MM-dd}",
                    DetailJson = JsonSerializer.Serialize(new { count = under.Count, channel = rule.DestinationChannel.Name }),
                    Outcome = sent ? ChaseOutcome.Sent : ChaseOutcome.Failed,
                    MessageText = summary,
                    DeliveredTo = $"#{rule.DestinationChannel.Name}",
                    CreatedAtUtc = DateTime.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private static string BuildChannelSummary(DateOnly date, double threshold, List<(TeamMember member, double hours)> under)
    {
        var sb = new StringBuilder();
        sb.Append($"<b>Time not logged for {date:yyyy-MM-dd}</b> (under {threshold:0.##}h):<br>");
        foreach (var (m, h) in under)
            sb.Append($"• {m.DisplayName} — {h:0.##}h<br>");
        sb.Append("<br>Please log your time in Jira.");
        return sb.ToString();
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

    /// <summary>Start of the current half-day (in the team timezone) and the matching default message.</summary>
    private static (DateTime SlotStartUtc, string Template) ResolveSlot(string tz)
    {
        var zone = WorkingDays.ResolveZone(tz);
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        var isMorning = local.Hour < 12;
        var slotStartLocal = new DateTimeOffset(local.Year, local.Month, local.Day, isMorning ? 0 : 12, 0, 0, local.Offset);
        return (slotStartLocal.UtcDateTime, isMorning ? MorningTemplate : AfternoonTemplate);
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
