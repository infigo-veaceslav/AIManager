using AIManager.Api.Domain;

namespace AIManager.Api.Services.Settings;

public interface ISettingsService
{
    /// <summary>Values actually in use (DB overrides merged with appsettings/env fallback).</summary>
    Task<EffectiveSettings> GetEffectiveAsync(CancellationToken ct = default);

    /// <summary>The persisted settings row (created empty on first access).</summary>
    Task<AppSettings> GetRawAsync(CancellationToken ct = default);

    /// <summary>Apply a patch. null = unchanged; "" = clear override.</summary>
    Task UpdateAsync(SettingsUpdate update, CancellationToken ct = default);
}
