using AIManager.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIManager.Api.Controllers;

[ApiController]
[Route("api/findings")]
public class FindingsController : ControllerBase
{
    private readonly AppDbContext _db;
    public FindingsController(AppDbContext db) => _db = db;

    public record FindingDto(int Id, DateOnly TargetDate, string IssueKey, string? Summary, string? Assignee,
        string? AssigneeEmail, string? Status, bool HasWorkDone, bool HasRemaining, bool HasEstimate,
        bool IsComplete, string? Missing, string? Verdict, DateTime CreatedAtUtc);

    /// <summary>Findings for a given target date (defaults to the most recent evaluated date).</summary>
    [HttpGet]
    public async Task<ActionResult<List<FindingDto>>> Get([FromQuery] DateOnly? date, [FromQuery] bool onlyIncomplete = false)
    {
        var targetDate = date ?? await _db.TaskUpdateFindings
            .OrderByDescending(f => f.TargetDate).Select(f => (DateOnly?)f.TargetDate).FirstOrDefaultAsync();
        if (targetDate is null) return new List<FindingDto>();

        var q = _db.TaskUpdateFindings.Where(f => f.TargetDate == targetDate);
        if (onlyIncomplete) q = q.Where(f => !(f.HasWorkDone && f.HasRemaining && f.HasEstimate));

        return await q.OrderBy(f => f.Assignee).ThenBy(f => f.IssueKey)
            .Select(f => new FindingDto(f.Id, f.TargetDate, f.IssueKey, f.Summary, f.Assignee, f.AssigneeEmail,
                f.Status, f.HasWorkDone, f.HasRemaining, f.HasEstimate,
                f.HasWorkDone && f.HasRemaining && f.HasEstimate, f.Missing, f.Verdict, f.CreatedAtUtc))
            .ToListAsync();
    }
}
