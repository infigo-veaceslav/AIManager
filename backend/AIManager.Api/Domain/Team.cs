namespace AIManager.Api.Domain;

/// <summary>A group of people that shares a roster, timezone, and set of chase rules.</summary>
public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>IANA timezone used for "previous working day" and worklog date comparisons.</summary>
    public string Timezone { get; set; } = "Europe/Chisinau";

    /// <summary>
    /// Working days as a CSV of System.DayOfWeek ints (Sun=0 … Sat=6). Default Mon–Thu ("1,2,3,4")
    /// since the team doesn't work Fridays. Drives "previous working day" for chasing.
    /// </summary>
    public string WorkingDays { get; set; } = "1,2,3,4";

    public bool Enabled { get; set; } = true;

    public List<TeamMember> Members { get; set; } = new();
    public List<ChaseRule> Rules { get; set; } = new();
}
