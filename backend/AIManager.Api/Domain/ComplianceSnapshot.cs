namespace AIManager.Api.Domain;

/// <summary>
/// Cached per-member logged hours for a team + working day, so the dashboard renders without hitting
/// Jira. Written by an explicit Resync and by the daily chaser; read by GET /api/compliance.
/// </summary>
public class ComplianceSnapshot
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public DateOnly TargetDate { get; set; }
    public int MemberId { get; set; }
    public double Hours { get; set; }
    public DateTime SyncedAtUtc { get; set; }
}
