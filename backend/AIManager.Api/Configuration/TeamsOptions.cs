namespace AIManager.Api.Configuration;

public class TeamsOptions
{
    public const string SectionName = "Teams";

    /// <summary>
    /// Power Automate HTTP-trigger URL for a flow that posts a 1:1 chat message as the user.
    /// The flow expects JSON: { "recipient": "...", "message": "..." }.
    /// </summary>
    public string PowerAutomateDmUrl { get; set; } = string.Empty;

    /// <summary>Operator address that "Report" channel rules and daily digests are sent to.</summary>
    public string ReportRecipient { get; set; } = string.Empty;
}
