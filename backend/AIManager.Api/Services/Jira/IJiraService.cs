namespace AIManager.Api.Services.Jira;

public interface IJiraService
{
    /// <summary>Resolve a Jira Cloud accountId from an email, or null if not found.</summary>
    Task<string?> ResolveAccountIdAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Total hours logged on <paramref name="date"/> (in <paramref name="timezone"/>) per author email,
    /// restricted to the given worklog-author accountIds. Emails are lowercased keys.
    /// </summary>
    Task<Dictionary<string, double>> GetWorklogHoursByEmailAsync(
        DateOnly date, IReadOnlyCollection<string> accountIds, string timezone, CancellationToken ct = default);

    /// <summary>
    /// Issues that are currently Active OR had a worklog on <paramref name="date"/>.
    /// <paramref name="scopeJql"/> is ANDed in (e.g. project restriction); pass null for none.
    /// </summary>
    Task<List<IssueSummary>> GetActiveOrWorkloggedIssuesAsync(
        DateOnly date, string? scopeJql, CancellationToken ct = default);

    /// <summary>Comments on an issue created on/after <paramref name="since"/>, newest first.</summary>
    Task<List<IssueComment>> GetCommentsAsync(
        string issueKey, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>
    /// Support issues in the given project and statuses (with assignee + reporter). When
    /// <paramref name="reporterEmails"/> is non-empty, only issues from those reporters are returned.
    /// <paramref name="maxIssues"/> = 0 means no cap.
    /// </summary>
    Task<List<SupportIssue>> GetSupportIssuesAsync(
        string projectKey, IReadOnlyCollection<string> statuses, IReadOnlyCollection<string> reporterEmails,
        int maxIssues, CancellationToken ct = default);
}
