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

    // Active = in the chase scope (only Active members get DMed).
    public record ComplianceRow(int MemberId, string DisplayName, string Email, double Hours, bool Ok, bool Active);
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

        return new ComplianceResult(team.Id, team.Name, target, threshold, lastSynced,
            BuildRows(members, hoursById, threshold));
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

        return new ComplianceResult(team.Id, team.Name, target, threshold, now,
            BuildRows(members, hoursById, threshold));
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

    private static List<ComplianceRow> BuildRows(List<TeamMember> members, IReadOnlyDictionary<int, double> hoursById, double threshold) =>
        members
            .Select(m =>
            {
                var hours = hoursById.TryGetValue(m.Id, out var h) ? h : 0.0;
                return new ComplianceRow(m.Id, m.DisplayName, m.Email, Math.Round(hours, 2), hours >= threshold, m.Active);
            })
            .OrderByDescending(r => r.Active)
            .ThenBy(r => r.Hours)
            .ToList();
}
