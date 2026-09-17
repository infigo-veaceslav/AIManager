using AIManager.Api.Services.Jira;

namespace AIManager.Api.Services.Ai;

public record UpdateJudgement(
    bool HasWorkDone,
    bool HasRemaining,
    bool HasEstimate,
    IReadOnlyList<string> Missing,
    string Verdict);

public interface IUpdateJudge
{
    /// <summary>
    /// Judges whether the recent comments on a ticket contain a proper progress update:
    /// (1) work done, (2) remaining work, (3) an estimate of the remaining part.
    /// </summary>
    Task<UpdateJudgement> JudgeAsync(
        string issueKey, string summary, IReadOnlyList<IssueComment> comments, CancellationToken ct = default);
}
