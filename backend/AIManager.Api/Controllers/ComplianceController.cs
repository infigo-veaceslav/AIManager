using AIManager.Api.Data;
using AIManager.Api.Domain;
using AIManager.Api.Services;
using AIManager.Api.Services.Compliance;
using AIManager.Api.Services.Jira;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIManager.Api.Controllers;

[ApiController]
[Route("api/compliance")]
public class ComplianceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IJiraService _jira;
    private readonly IComplianceSnapshotStore _snapshots;

    public ComplianceController(AppDbContext db, IJiraService jira, IComplianceSnapshotStore snapshots)
    {
        _db = db;
        _jira = jira;
        _snapshots = snapshots;
    }

    // Active = in the chase scope; Chased = a reminder was actually Sent for the target day.
    public record ComplianceRow(int MemberId, string DisplayName, string Email, double Hours, bool Ok, bool Active,
        bool Chased, DateTime? ChasedAt);
    public record ComplianceResult(int TeamId, string TeamName, DateOnly TargetDate, double Threshold,
        DateTime? LastSyncedAt, List<ComplianceRow> Rows);

    /// <summary>
    /// Reads the cached compliance snapshot for the previous working day (or a given date).
    /// Never hits Jira — returns instantly. LastSyncedAt is null until the first Resync / chaser run.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ComplianceResult>> Get([FromQuery] int? teamId, [FromQuery] DateOnly? date)
    {
        var ctx = await LoadContextAsync(teamId, date);
        if (ctx is null) return NotFound();
        var (team, target, threshold, members) = ctx.Value;

        var snapshot = await _snapshots.GetAsync(team.Id, target);
        var hoursById = snapshot.ToDictionary(s => s.MemberId, s => s.Hours);
        DateTime? lastSynced = snapshot.Count > 0 ? snapshot.Max(s => s.SyncedAtUtc) : null;
        var chased = await GetChasedAsync(team.Id, target);

        return new ComplianceResult(team.Id, team.Name, target, threshold, lastSynced,
            BuildRows(members, hoursById, threshold, chased));
    }

    /// <summary>
    /// Forces a live Jira query, upserts the snapshot, and returns the fresh result.
    /// The only path that hits Jira (inherits the 429 fail-safe → 503).
    /// </summary>
    [HttpPost("resync")]
    public async Task<ActionResult<ComplianceResult>> Resync([FromQuery] int? teamId, [FromQuery] DateOnly? date)
    {
        var ctx = await LoadContextAsync(teamId, date);
        if (ctx is null) return NotFound();
        var (team, target, threshold, members) = ctx.Value;

        Dictionary<string, double> hoursByEmail;
        try
        {
            // Resolve any missing accountIds so the worklog query is complete.
            var changed = false;
            foreach (var m in members.Where(m => string.IsNullOrEmpty(m.JiraAccountId)))
            {
                var id = await _jira.ResolveAccountIdAsync(m.Email);
                if (!string.IsNullOrEmpty(id)) { m.JiraAccountId = id; changed = true; }
            }
            if (changed) await _db.SaveChangesAsync();

            var accountIds = members.Where(m => m.JiraAccountId != null).Select(m => m.JiraAccountId!).ToList();
            hoursByEmail = await _jira.GetWorklogHoursByEmailAsync(target, accountIds, team.Timezone);
        }
        catch (JiraUnavailableException)
        {
            return StatusCode(503, new { message = "Jira is throttling or unavailable right now — please try again shortly." });
        }

        var hoursById = members.ToDictionary(m => m.Id, m => hoursByEmail.TryGetValue(m.Email, out var h) ? h : 0.0);
        var now = DateTime.UtcNow;
        await _snapshots.UpsertAsync(team.Id, target, hoursById, now);
        var chased = await GetChasedAsync(team.Id, target);

        return new ComplianceResult(team.Id, team.Name, target, threshold, now,
            BuildRows(members, hoursById, threshold, chased));
    }

    private async Task<(Team Team, DateOnly Target, double Threshold, List<TeamMember> Members)?> LoadContextAsync(
        int? teamId, DateOnly? date)
    {
        var team = teamId is null
            ? await _db.Teams.Include(t => t.Members).FirstOrDefaultAsync()
            : await _db.Teams.Include(t => t.Members).FirstOrDefaultAsync(t => t.Id == teamId);
        if (team is null) return null;

        var target = date ?? WorkingDays.PreviousWorkingDay(team.Timezone, team.WorkingDays, DateTimeOffset.UtcNow);
        var threshold = await _db.ChaseRules
            .Where(r => r.TeamId == team.Id && r.Type == ChaseRuleType.TimeLog)
            .Select(r => r.ThresholdHours).FirstOrDefaultAsync() ?? 5.0;

        return (team, target, threshold, team.Members.ToList());
    }

    private static List<ComplianceRow> BuildRows(List<TeamMember> members, IReadOnlyDictionary<int, double> hoursById,
        double threshold, IReadOnlyDictionary<int, DateTime> chased) =>
        members
            .Select(m =>
            {
                var hours = hoursById.TryGetValue(m.Id, out var h) ? h : 0.0;
                var wasChased = chased.TryGetValue(m.Id, out var at);
                return new ComplianceRow(m.Id, m.DisplayName, m.Email, Math.Round(hours, 2), hours >= threshold, m.Active,
                    wasChased, wasChased ? at : null);
            })
            .OrderByDescending(r => r.Active)
            .ThenBy(r => r.Hours)
            .ToList();

    /// <summary>Members Sent a TimeLog reminder for this target day → latest send time.</summary>
    private async Task<Dictionary<int, DateTime>> GetChasedAsync(int teamId, DateOnly target)
    {
        var ruleIds = await _db.ChaseRules
            .Where(r => r.TeamId == teamId && r.Type == ChaseRuleType.TimeLog)
            .Select(r => r.Id).ToListAsync();
        if (ruleIds.Count == 0) return new Dictionary<int, DateTime>();

        return await _db.ChaseEvents
            .Where(e => ruleIds.Contains(e.RuleId) && e.TargetDate == target
                        && e.Outcome == ChaseOutcome.Sent && e.MemberId != null)
            .GroupBy(e => e.MemberId!.Value)
            .Select(g => new { MemberId = g.Key, Last = g.Max(x => x.CreatedAtUtc) })
            .ToDictionaryAsync(x => x.MemberId, x => x.Last);
    }
}
