using System.Text;
using System.Text.Json;
using AIManager.Api.Services.Settings;

namespace AIManager.Api.Services.Teams;

/// <summary>
/// Delivers Teams messages through a single Power Automate flow that switches on a "type" field:
/// "user" → 1:1 DM as the operator, "channel" → channel post.
/// </summary>
public class TeamsService : ITeamsService
{
    private readonly HttpClient _http;
    private readonly ISettingsService _settings;
    private readonly ILogger<TeamsService> _log;

    public TeamsService(HttpClient http, ISettingsService settings, ILogger<TeamsService> log)
    {
        _http = http;
        _settings = settings;
        _log = log;
    }

    public Task<bool> SendDmAsync(string recipientEmail, string htmlMessage, CancellationToken ct = default) =>
        PostAsync(new { type = "user", recipient = recipientEmail, message = htmlMessage }, $"DM {recipientEmail}", ct);

    public Task<bool> SendChannelAsync(string teamId, string channelId, string htmlMessage, CancellationToken ct = default) =>
        PostAsync(new { type = "channel", teamId, channelId, message = htmlMessage }, $"channel {teamId}/{channelId}", ct);

    public Task<bool> SendChannelWithMentionsAsync(
        string teamId, string channelId, string htmlMessage, IReadOnlyList<MentionTarget> people, CancellationToken ct = default) =>
        PostAsync(new
        {
            type = "channelMention",
            teamId,
            channelId,
            message = htmlMessage,
            people = people.Select(p => new { index = p.Index, email = p.Email, displayName = p.DisplayName })
        }, $"channelMention {teamId}/{channelId} ({people.Count} mentioned)", ct);

    private async Task<bool> PostAsync(object payload, string description, CancellationToken ct)
    {
        var url = (await _settings.GetEffectiveAsync(ct)).Teams.PowerAutomateDmUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            _log.LogWarning("Teams send skipped: Power Automate flow URL is not configured (target: {Target}).", description);
            return false;
        }

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        try
        {
            using var resp = await _http.PostAsync(url, content, ct);
            if (resp.IsSuccessStatusCode)
            {
                _log.LogInformation("Teams message delivered ({Target}).", description);
                return true;
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            _log.LogWarning("Teams send to {Target} failed: {Status} {Body}", description, (int)resp.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Teams send to {Target} threw.", description);
            return false;
        }
    }
}
