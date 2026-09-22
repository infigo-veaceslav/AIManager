using AIManager.Api.Data;
using AIManager.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AIManager.Api.Services.Compliance;

public class ComplianceSnapshotStore : IComplianceSnapshotStore
{
    private readonly AppDbContext _db;
    public ComplianceSnapshotStore(AppDbContext db) => _db = db;

    public Task<List<ComplianceSnapshot>> GetAsync(int teamId, DateOnly date, CancellationToken ct = default) =>
        _db.ComplianceSnapshots.Where(s => s.TeamId == teamId && s.TargetDate == date).ToListAsync(ct);

    public async Task UpsertAsync(int teamId, DateOnly date, IReadOnlyDictionary<int, double> memberHours,
        DateTime syncedAtUtc, CancellationToken ct = default)
    {
        if (memberHours.Count == 0) return;

        var ids = memberHours.Keys.ToList();
        var existing = await _db.ComplianceSnapshots
            .Where(s => s.TeamId == teamId && s.TargetDate == date && ids.Contains(s.MemberId))
            .ToListAsync(ct);
        var byMember = existing.ToDictionary(s => s.MemberId);

        foreach (var (memberId, hours) in memberHours)
        {
            if (byMember.TryGetValue(memberId, out var row))
            {
                row.Hours = hours;
                row.SyncedAtUtc = syncedAtUtc;
            }
            else
            {
                _db.ComplianceSnapshots.Add(new ComplianceSnapshot
                {
                    TeamId = teamId, TargetDate = date, MemberId = memberId, Hours = hours, SyncedAtUtc = syncedAtUtc
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
