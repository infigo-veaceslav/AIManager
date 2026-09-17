using AIManager.Api.Services.Settings;
using Microsoft.AspNetCore.Mvc;

namespace AIManager.Api.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _settings;
    public SettingsController(ISettingsService settings) => _settings = settings;

    public record JiraDto(string Url, string User, bool ApiTokenSet);
    public record TeamsDto(bool PowerAutomateDmUrlSet, string ReportRecipient);
    public record AnthropicDto(bool ApiKeySet, string BaseUrl, string Model, int MaxTokens);
    public record SettingsDto(JiraDto Jira, TeamsDto Teams, AnthropicDto Anthropic);

    /// <summary>
    /// Effective settings actually in use. Non-secret values are returned in full; secrets are
    /// reported only as set / not-set so they are never echoed back to the client.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<SettingsDto>> Get()
    {
        var e = await _settings.GetEffectiveAsync();
        return new SettingsDto(
            new JiraDto(e.Jira.Url, e.Jira.User, !string.IsNullOrWhiteSpace(e.Jira.ApiToken)),
            new TeamsDto(!string.IsNullOrWhiteSpace(e.Teams.PowerAutomateDmUrl), e.Teams.ReportRecipient),
            new AnthropicDto(!string.IsNullOrWhiteSpace(e.Anthropic.ApiKey), e.Anthropic.BaseUrl,
                e.Anthropic.Model, e.Anthropic.MaxTokens));
    }

    /// <summary>
    /// Update overrides. Any field omitted (null) is left unchanged; an empty string clears the
    /// override so the value falls back to appsettings/environment.
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] SettingsUpdate update)
    {
        await _settings.UpdateAsync(update);
        return NoContent();
    }
}
