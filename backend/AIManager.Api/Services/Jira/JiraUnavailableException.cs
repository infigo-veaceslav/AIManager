namespace AIManager.Api.Services.Jira;

/// <summary>
/// Thrown when Jira is throttling (429) or persistently failing (5xx/network) after retries.
/// Callers should treat this as "unknown data" and skip — never as "0 hours" — to avoid acting on
/// incomplete data (e.g. DMing someone who actually logged time).
/// </summary>
public class JiraUnavailableException : Exception
{
    public JiraUnavailableException(string message) : base(message) { }
    public JiraUnavailableException(string message, Exception inner) : base(message, inner) { }
}
