namespace AIManager.Api.Configuration;

public class JiraOptions
{
    public const string SectionName = "Jira";

    /// <summary>Base URL, e.g. https://infigosoftware.atlassian.net</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Account email used for basic auth.</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>API token (basic auth password).</summary>
    public string ApiToken { get; set; } = string.Empty;
}
