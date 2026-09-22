namespace AIManager.Api.Domain;

/// <summary>One recorded chase attempt — the history the dashboard renders.</summary>
public class ChaseEvent
{
    public int Id { get; set; }

    public int RuleId { get; set; }
    public ChaseRule? Rule { get; set; }

    public int? MemberId { get; set; }
    public TeamMember? Member { get; set; }

    /// <summary>The working day the chase was about (not the day it ran).</summary>
    public DateOnly TargetDate { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>Optional structured detail (e.g. hours logged, issue keys) as JSON.</summary>
    public string? DetailJson { get; set; }

    public ChaseOutcome Outcome { get; set; }

    /// <summary>Whether the run was fired by the schedule or the Run-now button.</summary>
    public ChaseTrigger Trigger { get; set; }

    /// <summary>The exact message body sent (or that would have been sent).</summary>
    public string? MessageText { get; set; }

    /// <summary>Where the message actually went (honors TestRecipientOverride).</summary>
    public string? DeliveredTo { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
