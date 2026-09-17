namespace AIManager.Api.Services.Settings;

public record EffectiveJira(string Url, string User, string ApiToken);
public record EffectiveTeams(string PowerAutomateDmUrl, string ReportRecipient);
public record EffectiveAnthropic(string ApiKey, string BaseUrl, string Model, int MaxTokens);

/// <summary>Resolved configuration in use right now (DB overrides, appsettings/env fallback).</summary>
public record EffectiveSettings(EffectiveJira Jira, EffectiveTeams Teams, EffectiveAnthropic Anthropic);

/// <summary>Patch for updating settings. null = leave unchanged; "" = clear override (fall back to config).</summary>
public record SettingsUpdate(
    string? JiraUrl = null,
    string? JiraUser = null,
    string? JiraApiToken = null,
    string? TeamsPowerAutomateDmUrl = null,
    string? TeamsReportRecipient = null,
    string? AnthropicApiKey = null,
    string? AnthropicBaseUrl = null,
    string? AnthropicModel = null,
    int? AnthropicMaxTokens = null);
