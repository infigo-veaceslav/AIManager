namespace AIManager.Api.Domain;

/// <summary>
/// Result of judging one ticket's recent comments for a proper progress update
/// (work done / remaining work / estimate of the remaining part).
/// </summary>
public class TaskUpdateFinding
{
    public int Id { get; set; }

    /// <summary>The working day this evaluation covered.</summary>
    public DateOnly TargetDate { get; set; }

    public string IssueKey { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Assignee { get; set; }
    public string? AssigneeEmail { get; set; }
    public string? Status { get; set; }

    public bool HasWorkDone { get; set; }
    public bool HasRemaining { get; set; }
    public bool HasEstimate { get; set; }

    /// <summary>True when all three parts are present.</summary>
    public bool IsComplete => HasWorkDone && HasRemaining && HasEstimate;

    /// <summary>Comma-separated list of the missing parts.</summary>
    public string? Missing { get; set; }

    /// <summary>One-line LLM rationale.</summary>
    public string? Verdict { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
