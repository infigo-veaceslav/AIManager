using AIManager.Api.Data;
using AIManager.Api.Domain;
using AIManager.Api.Jobs;
using AIManager.Api.Services;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIManager.Api.Controllers;

[ApiController]
[Route("api/rules")]
public class RulesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IBackgroundJobClient _jobs;
    private readonly IRecurringJobManager _recurring;

    public RulesController(AppDbContext db, IBackgroundJobClient jobs, IRecurringJobManager recurring)
    {
        _db = db;
        _jobs = jobs;
        _recurring = recurring;
    }

    public record RuleDto(int Id, int TeamId, string TeamName, string Type, bool Enabled, string Cron,
        double? ThresholdHours, string? MessageTemplate, string Channel, string? TestRecipientOverride,
        string? ConfigJson, string DeliveryMode, int? DestinationChannelId, string? DestinationChannelName);

    public record UpdateRuleRequest(bool? Enabled, string? Cron, double? ThresholdHours,
        string? MessageTemplate, string? TestRecipientOverride, string? ConfigJson,
        string? DeliveryMode, int? DestinationChannelId);

    [HttpGet]
    public async Task<ActionResult<List<RuleDto>>> Get()
    {
        return await _db.ChaseRules.Include(r => r.Team).Include(r => r.DestinationChannel)
            .Select(r => new RuleDto(r.Id, r.TeamId, r.Team!.Name, r.Type.ToString(), r.Enabled, r.Cron,
                r.ThresholdHours, r.MessageTemplate, r.Channel.ToString(), r.TestRecipientOverride, r.ConfigJson,
                r.DeliveryMode.ToString(), r.DestinationChannelId,
                r.DestinationChannel != null ? r.DestinationChannel.Name : null))
            .ToListAsync();
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateRuleRequest req)
    {
        var rule = await _db.ChaseRules.Include(r => r.Team).FirstOrDefaultAsync(r => r.Id == id);
        if (rule is null) return NotFound();

        if (req.Enabled is not null) rule.Enabled = req.Enabled.Value;
        if (req.Cron is not null) rule.Cron = req.Cron;
        if (req.ThresholdHours is not null) rule.ThresholdHours = req.ThresholdHours;
        if (req.MessageTemplate is not null) rule.MessageTemplate = req.MessageTemplate;
        // Empty string clears the test override (go-live); null leaves it unchanged.
        if (req.TestRecipientOverride is not null)
            rule.TestRecipientOverride = req.TestRecipientOverride.Length == 0 ? null : req.TestRecipientOverride;
        if (req.ConfigJson is not null) rule.ConfigJson = req.ConfigJson;
        if (req.DeliveryMode is not null && Enum.TryParse<DeliveryMode>(req.DeliveryMode, true, out var dm))
            rule.DeliveryMode = dm;
        // 0 or negative clears the destination channel; a positive id sets it.
        if (req.DestinationChannelId is not null)
            rule.DestinationChannelId = req.DestinationChannelId.Value <= 0 ? null : req.DestinationChannelId.Value;

        await _db.SaveChangesAsync();
        SyncRecurringJob(rule);
        return NoContent();
    }

    /// <summary>Enqueue an immediate run of the rule (for validation). Returns the Hangfire job id.</summary>
    [HttpPost("{id:int}/run")]
    public async Task<IActionResult> RunNow(int id)
    {
        var rule = await _db.ChaseRules.FindAsync(id);
        if (rule is null) return NotFound();

        var jobId = rule.Type switch
        {
            ChaseRuleType.TimeLog => _jobs.Enqueue<TimeLogChaserJob>(j => j.RunRuleAsync(id, true, CancellationToken.None)),
            ChaseRuleType.TaskUpdate => _jobs.Enqueue<TaskUpdateTrackerJob>(j => j.RunRuleAsync(id, true, CancellationToken.None)),
            ChaseRuleType.SupportDigest => _jobs.Enqueue<SupportDigestJob>(j => j.RunRuleAsync(id, true, CancellationToken.None)),
            _ => null
        };
        return Accepted(new { jobId });
    }

    private void SyncRecurringJob(ChaseRule rule) => RecurringJobs.Sync(_recurring, rule);
}
