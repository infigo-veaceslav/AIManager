namespace AIManager.Api.Services.Jira;

public record IssueSummary(
    string Key,
    string Summary,
    string Status,
    string? AssigneeName,
    string? AssigneeEmail);

public record IssueComment(
    string? AuthorName,
    string? AuthorEmail,
    DateTimeOffset Created,
    string Text);

public record SupportIssue(
    string Key,
    string Summary,
    string Status,
    string? AssigneeName,
    string? AssigneeEmail,
    string? ReporterName,
    string? ReporterEmail,
    DateOnly? DueDate);

public record JiraPerson(string? AccountId, string DisplayName, string Email);
