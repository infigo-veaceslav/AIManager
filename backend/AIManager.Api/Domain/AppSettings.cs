namespace AIManager.Api.Domain;

/// <summary>
/// Single-row (Id = 1) runtime settings, editable from the dashboard. Any field left null/blank
/// falls back to appsettings/environment configuration. Secrets are stored as-is (internal MVP).
/// </summary>
public class AppSettings
{
    public int Id { get; set; } = 1;

    public string? JiraUrl { get; set; }
    public string? JiraUser { get; set; }
    public string? JiraApiToken { get; set; }

    public string? TeamsPowerAutomateDmUrl { get; set; }
    public string? TeamsReportRecipient { get; set; }

    public string? AnthropicApiKey { get; set; }
    public string? AnthropicBaseUrl { get; set; }
    public string? AnthropicModel { get; set; }
    public int? AnthropicMaxTokens { get; set; }

    /// <summary>Comma-separated Jira project keys the Users sync scans (e.g. "SUP,VENTURE").</summary>
    public string? UserSyncProjectKeys { get; set; }

    /// <summary>Comma-separated Jira agile board ids the Users sync scans (e.g. the DR board).</summary>
    public string? UserSyncBoardIds { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
}
