using AIManager.Api.Domain;
using AIManager.Api.Services;
using Hangfire;

namespace AIManager.Api.Jobs;

/// <summary>
/// Registers a rule's Hangfire recurring jobs. A rule's Cron may hold several ';'-separated cron
/// expressions (e.g. "45 9 * * 1-4;0 14 * * 1-4") — each becomes its own recurring trigger.
/// </summary>
public static class RecurringJobs
{
    private const int MaxSchedules = 10;

    public static void Sync(IRecurringJobManager manager, ChaseRule rule)
    {
        var baseId = $"rule-{rule.Id}-{rule.Type}".ToLowerInvariant();

        // Clear the legacy single-id job and every indexed slot before re-registering.
        manager.RemoveIfExists(baseId);
        for (var i = 0; i < MaxSchedules; i++) manager.RemoveIfExists($"{baseId}-{i}");

        if (!rule.Enabled) return;

        var options = new RecurringJobOptions { TimeZone = WorkingDays.ResolveZone(rule.Team?.Timezone ?? "UTC") };
        var crons = rule.Cron.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var i = 0; i < crons.Length && i < MaxSchedules; i++)
        {
            var jobId = $"{baseId}-{i}";
            var cron = crons[i];
            switch (rule.Type)
            {
                case ChaseRuleType.TimeLog:
                    manager.AddOrUpdate<TimeLogChaserJob>(jobId, j => j.RunRuleAsync(rule.Id, false, CancellationToken.None), cron, options);
                    break;
                case ChaseRuleType.TaskUpdate:
                    manager.AddOrUpdate<TaskUpdateTrackerJob>(jobId, j => j.RunRuleAsync(rule.Id, false, CancellationToken.None), cron, options);
                    break;
                case ChaseRuleType.SupportDigest:
                    manager.AddOrUpdate<SupportDigestJob>(jobId, j => j.RunRuleAsync(rule.Id, false, CancellationToken.None), cron, options);
                    break;
            }
        }
    }
}
