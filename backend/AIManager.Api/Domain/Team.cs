namespace AIManager.Api.Domain;

/// <summary>A group of people that shares a roster, timezone, and set of chase rules.</summary>
public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>IANA timezone used for "previous working day" and worklog date comparisons.</summary>
    public string Timezone { get; set; } = "Europe/Chisinau";

    public bool Enabled { get; set; } = true;

    public List<TeamMember> Members { get; set; } = new();
    public List<ChaseRule> Rules { get; set; } = new();
}
