namespace AIManager.Api.Domain;

/// <summary>
/// A person known to the platform — the master directory used to pick reporters/assignees for rules.
/// Populated by Jira sync, dev-roster import, or manual add. Independent of TeamMembers.
/// </summary>
public class User
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? JiraAccountId { get; set; }
    public bool Active { get; set; } = true;

    /// <summary>Where the record came from: "jira", "roster", or "manual".</summary>
    public string Source { get; set; } = "manual";

    public DateTime UpdatedAtUtc { get; set; }
}
