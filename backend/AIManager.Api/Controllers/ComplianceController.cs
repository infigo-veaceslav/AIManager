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

    public record ComplianceRow(int MemberId, string DisplayName, string Email, double Hours, bool Ok);
    public record ComplianceResult(int TeamId, string TeamName, DateOnly TargetDate, double Threshold, List<ComplianceRow> Rows);

    /// <summary>
    /// Live per-member logged hours for the previous working day (or a given date), vs the team's
    /// TimeLog threshold. Queries Jira on demand.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ComplianceResult>> Get([FromQuery] int? teamId, [FromQuery] DateOnly? date)
    {
        var team = teamId is null
            ? await _db.Teams.Include(t => t.Members).FirstOrDefaultAsync()
            : await _db.Teams.Include(t => t.Members).FirstOrDefaultAsync(t => t.Id == teamId);
        if (team is null) return NotFound();

        var target = date ?? WorkingDays.PreviousWorkingDay(team.Timezone, DateTimeOffset.UtcNow);
        var threshold = await _db.ChaseRules
            .Where(r => r.TeamId == team.Id && r.Type == ChaseRuleType.TimeLog)
            .Select(r => r.ThresholdHours).FirstOrDefaultAsync() ?? 5.0;

        var members = team.Members.Where(m => m.Active).ToList();

        // Resolve any missing accountIds so the worklog query is complete.
        var changed = false;
        foreach (var m in members.Where(m => string.IsNullOrEmpty(m.JiraAccountId)))
        {
            var id = await _jira.ResolveAccountIdAsync(m.Email);
            if (!string.IsNullOrEmpty(id)) { m.JiraAccountId = id; changed = true; }
        }
        if (changed) await _db.SaveChangesAsync();

        var accountIds = members.Where(m => m.JiraAccountId != null).Select(m => m.JiraAccountId!).ToList();
        var hoursByEmail = await _jira.GetWorklogHoursByEmailAsync(target, accountIds, team.Timezone);

        var rows = members
            .Select(m =>
            {
                var hours = hoursByEmail.TryGetValue(m.Email, out var h) ? h : 0.0;
                return new ComplianceRow(m.Id, m.DisplayName, m.Email, Math.Round(hours, 2), hours >= threshold);
            })
            .OrderBy(r => r.Hours)
            .ToList();

        return new ComplianceResult(team.Id, team.Name, target, threshold, rows);
    }
}
