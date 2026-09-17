namespace AIManager.Api.Domain;

/// <summary>
/// A configurable nudge. Kept deliberately generic (type + cron + threshold + JSON config +
/// message template) so new chase types and per-team tuning are config, not code.
/// </summary>
public class ChaseRule
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public Team? Team { get; set; }

    public ChaseRuleType Type { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Hangfire cron expression for the recurring schedule.</summary>
    public string Cron { get; set; } = "0 10 * * 1-5"; // 10:00 Mon-Fri (team timezone)

    /// <summary>For TimeLog: minimum hours expected for the previous working day.</summary>
    public double? ThresholdHours { get; set; }

    /// <summary>Free-form JSON for type-specific settings (e.g. JQL overrides).</summary>
    public string? ConfigJson { get; set; }

    /// <summary>Message template; supports {name}, {firstName}, {date}, {hours} placeholders.</summary>
    public string? MessageTemplate { get; set; }

    public ChaseChannel Channel { get; set; } = ChaseChannel.TeamsDm;

    /// <summary>
    /// While validating: if set, every DM is redirected to this email instead of the real
    /// recipient. Clear it to go live.
    /// </summary>
    public string? TestRecipientOverride { get; set; }
}
