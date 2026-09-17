using AIManager.Api.Configuration;
using AIManager.Api.Data;
using AIManager.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIManager.Api.Services.Settings;

/// <summary>Scoped: resolves effective settings and persists overrides. Caches within the scope.</summary>
public class SettingsService : ISettingsService
{
    private readonly AppDbContext _db;
    private readonly JiraOptions _jira;
    private readonly TeamsOptions _teams;
    private readonly AnthropicOptions _anthropic;

    private AppSettings? _cachedRow;

    public SettingsService(AppDbContext db, IOptions<JiraOptions> jira,
        IOptions<TeamsOptions> teams, IOptions<AnthropicOptions> anthropic)
    {
        _db = db;
        _jira = jira.Value;
        _teams = teams.Value;
        _anthropic = anthropic.Value;
    }

    public async Task<AppSettings> GetRawAsync(CancellationToken ct = default)
    {
        if (_cachedRow is not null) return _cachedRow;

        var row = await _db.AppSettings.FirstOrDefaultAsync(s => s.Id == 1, ct);
        if (row is null)
        {
            row = new AppSettings { Id = 1 };
            _db.AppSettings.Add(row);
            await _db.SaveChangesAsync(ct);
        }
        return _cachedRow = row;
    }

    public async Task<EffectiveSettings> GetEffectiveAsync(CancellationToken ct = default)
    {
        var s = await GetRawAsync(ct);
        return new EffectiveSettings(
            new EffectiveJira(
                Pick(s.JiraUrl, _jira.Url),
                Pick(s.JiraUser, _jira.User),
                Pick(s.JiraApiToken, _jira.ApiToken)),
            new EffectiveTeams(
                Pick(s.TeamsPowerAutomateDmUrl, _teams.PowerAutomateDmUrl),
                Pick(s.TeamsReportRecipient, _teams.ReportRecipient)),
            new EffectiveAnthropic(
                Pick(s.AnthropicApiKey, _anthropic.ApiKey),
                Pick(s.AnthropicBaseUrl, _anthropic.BaseUrl),
                Pick(s.AnthropicModel, _anthropic.Model),
                s.AnthropicMaxTokens ?? _anthropic.MaxTokens));
    }

    public async Task UpdateAsync(SettingsUpdate u, CancellationToken ct = default)
    {
        var s = await GetRawAsync(ct);
        s.JiraUrl = Apply(s.JiraUrl, u.JiraUrl);
        s.JiraUser = Apply(s.JiraUser, u.JiraUser);
        s.JiraApiToken = Apply(s.JiraApiToken, u.JiraApiToken);
        s.TeamsPowerAutomateDmUrl = Apply(s.TeamsPowerAutomateDmUrl, u.TeamsPowerAutomateDmUrl);
        s.TeamsReportRecipient = Apply(s.TeamsReportRecipient, u.TeamsReportRecipient);
        s.AnthropicApiKey = Apply(s.AnthropicApiKey, u.AnthropicApiKey);
        s.AnthropicBaseUrl = Apply(s.AnthropicBaseUrl, u.AnthropicBaseUrl);
        s.AnthropicModel = Apply(s.AnthropicModel, u.AnthropicModel);
        if (u.AnthropicMaxTokens is not null)
            s.AnthropicMaxTokens = u.AnthropicMaxTokens.Value <= 0 ? null : u.AnthropicMaxTokens;
        s.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>DB value wins unless blank, then fall back to config.</summary>
    private static string Pick(string? dbValue, string fallback) =>
        string.IsNullOrWhiteSpace(dbValue) ? fallback : dbValue;

    /// <summary>null = keep current; "" = clear override; otherwise set new value.</summary>
    private static string? Apply(string? current, string? update) =>
        update is null ? current : update.Length == 0 ? null : update;
}
