namespace AIManager.Api.Domain;

/// <summary>A person on a team. JiraAccountId is resolved from email on first use if missing.</summary>
public class TeamMember
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public Team? Team { get; set; }

    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>Jira Cloud accountId; resolved lazily from <see cref="Email"/> when null.</summary>
    public string? JiraAccountId { get; set; }

    public bool Active { get; set; } = true;
}
