using AIManager.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIManager.Api.Controllers;

[ApiController]
[Route("api/chase-events")]
public class ChaseEventsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ChaseEventsController(AppDbContext db) => _db = db;

    public record ChaseEventDto(int Id, int RuleId, string Type, int? MemberId, string? MemberName,
        DateOnly TargetDate, string Reason, string Outcome, string Trigger, string? DeliveredTo, string? MessageText,
        string? DetailJson, DateTime CreatedAtUtc);

    [HttpGet]
    public async Task<ActionResult<List<ChaseEventDto>>> Get(
        [FromQuery] int? ruleId, [FromQuery] int? memberId,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int take = 200)
    {
        var q = _db.ChaseEvents.Include(e => e.Rule).Include(e => e.Member).AsQueryable();
        if (ruleId is not null) q = q.Where(e => e.RuleId == ruleId);
        if (memberId is not null) q = q.Where(e => e.MemberId == memberId);
        if (from is not null) q = q.Where(e => e.TargetDate >= from);
        if (to is not null) q = q.Where(e => e.TargetDate <= to);

        return await q.OrderByDescending(e => e.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 1000))
            .Select(e => new ChaseEventDto(e.Id, e.RuleId, e.Rule!.Type.ToString(), e.MemberId,
                e.Member != null ? e.Member.DisplayName : null,
                e.TargetDate, e.Reason, e.Outcome.ToString(), e.Trigger.ToString(), e.DeliveredTo, e.MessageText,
                e.DetailJson, e.CreatedAtUtc))
            .ToListAsync();
    }
}
