using AIManager.Api.Data;
using AIManager.Api.Domain;
using AIManager.Api.Services;
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

    public ComplianceController(AppDbContext db, IJiraService jira)
    {
        _db = db;
        _jira = jira;
    }

    // Active = in the chase scope (only Active members get DMed).
    public record ComplianceRow(int MemberId, string DisplayName, string Email, double Hours, bool Ok, bool Active);
    public record ComplianceResult(int TeamId, string TeamName, DateOnly TargetDate, double Threshold, List<ComplianceRow> Rows);

    /// <summary>
    /// Live per-member logged hours for the previous working day (or a given date), vs the team's
    /// TimeLog threshold. Shows ALL members (active + inactive) for visibility; only Active members
    /// are in the chase scope. Queries Jira on demand.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ComplianceResult>> Get([FromQuery] int? teamId, [FromQuery] DateOnly? date)
    {
        var team = teamId is null
            ? await _db.Teams.Include(t => t.Members).FirstOrDefaultAsync()
            : await _db.Teams.Include(t => t.Members).FirstOrDefaultAsync(t => t.Id == teamId);
        if (team is null) return NotFound();

        var target = date ?? WorkingDays.PreviousWorkingDay(team.Timezone, team.WorkingDays, DateTimeOffset.UtcNow);
        var threshold = await _db.ChaseRules
            .Where(r => r.TeamId == team.Id && r.Type == ChaseRuleType.TimeLog)
            .Select(r => r.ThresholdHours).FirstOrDefaultAsync() ?? 5.0;

        // Everyone on the roster is shown; chasing is scoped to Active members elsewhere.
        var members = team.Members.ToList();

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
            // Better an explicit error than a table of misleading zeros.
            return StatusCode(503, new { message = "Jira is throttling or unavailable right now — please try again shortly." });
        }

        var rows = members
            .Select(m =>
            {
                var hours = hoursByEmail.TryGetValue(m.Email, out var h) ? h : 0.0;
                return new ComplianceRow(m.Id, m.DisplayName, m.Email, Math.Round(hours, 2), hours >= threshold, m.Active);
            })
            .OrderByDescending(r => r.Active)
            .ThenBy(r => r.Hours)
            .ToList();

        return new ComplianceResult(team.Id, team.Name, target, threshold, rows);
    }
}
