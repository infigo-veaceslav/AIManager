using AIManager.Api.Domain;

namespace AIManager.Api.Services.Compliance;

public interface IComplianceSnapshotStore
{
    Task<List<ComplianceSnapshot>> GetAsync(int teamId, DateOnly date, CancellationToken ct = default);

    /// <summary>Upserts per-member hours for a team + date (existing members' rows are updated, others left).</summary>
    Task UpsertAsync(int teamId, DateOnly date, IReadOnlyDictionary<int, double> memberHours, DateTime syncedAtUtc,
        CancellationToken ct = default);
}
