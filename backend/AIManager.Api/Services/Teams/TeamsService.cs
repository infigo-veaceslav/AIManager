using System.Text;
using System.Text.Json;
using AIManager.Api.Services.Settings;

namespace AIManager.Api.Services.Teams;

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

    public async Task<bool> SendDmAsync(string recipientEmail, string htmlMessage, CancellationToken ct = default)
    {
        var url = (await _settings.GetEffectiveAsync(ct)).Teams.PowerAutomateDmUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            _log.LogWarning("Teams DM skipped: Power Automate DM URL is not configured. Would have sent to {Recipient}.", recipientEmail);
            return false;
        }

        var payload = JsonSerializer.Serialize(new { recipient = recipientEmail, message = htmlMessage });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        try
        {
            using var resp = await _http.PostAsync(url, content, ct);
            if (resp.IsSuccessStatusCode)
            {
                _log.LogInformation("Teams DM delivered to {Recipient}.", recipientEmail);
                return true;
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            _log.LogWarning("Teams DM to {Recipient} failed: {Status} {Body}", recipientEmail, (int)resp.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Teams DM to {Recipient} threw.", recipientEmail);
            return false;
        }
    }
}
