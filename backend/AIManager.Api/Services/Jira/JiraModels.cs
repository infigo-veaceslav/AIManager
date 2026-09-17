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
