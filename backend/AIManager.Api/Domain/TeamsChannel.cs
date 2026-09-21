namespace AIManager.Api.Domain;

/// <summary>A registered Teams channel destination, referenced by rules for summaries/nudges.</summary>
public class TeamsChannel
{
    public int Id { get; set; }

    /// <summary>Friendly name shown in the UI (e.g. "Dev — Standup").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Teams group/team GUID (the groupId from the channel link).</summary>
    public string TeamId { get; set; } = string.Empty;

    /// <summary>Channel id (the channel segment from the channel link).</summary>
    public string ChannelId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
