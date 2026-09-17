using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIManager.Api.Services.Settings;

namespace AIManager.Api.Services.Jira;

/// <summary>Thin Jira Cloud REST v3 client. Base URL and basic-auth are resolved per request from settings.</summary>
public class JiraService : IJiraService
{
    private readonly HttpClient _http;
    private readonly ISettingsService _settings;
    private readonly ILogger<JiraService> _log;

    public JiraService(HttpClient http, ISettingsService settings, ILogger<JiraService> log)
    {
        _http = http;
        _settings = settings;
        _log = log;
    }

    public async Task<string?> ResolveAccountIdAsync(string email, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"rest/api/3/user/search?query={Uri.EscapeDataString(email)}&maxResults=5", ct);
        if (doc is null) return null;

        foreach (var u in doc.RootElement.EnumerateArray())
        {
            var mail = u.TryGetProperty("emailAddress", out var m) ? m.GetString() : null;
            if (!string.IsNullOrEmpty(mail) && string.Equals(mail, email, StringComparison.OrdinalIgnoreCase))
                return u.GetProperty("accountId").GetString();
        }
        // Fallback: first result (email may be hidden by privacy settings).
        var first = doc.RootElement.EnumerateArray().FirstOrDefault();
        return first.ValueKind == JsonValueKind.Object && first.TryGetProperty("accountId", out var a)
            ? a.GetString()
            : null;
    }

    public async Task<Dictionary<string, double>> GetWorklogHoursByEmailAsync(
        DateOnly date, IReadOnlyCollection<string> accountIds, string timezone, CancellationToken ct = default)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (accountIds.Count == 0) return result;

        var zone = WorkingDays.ResolveZone(timezone);
        var idList = string.Join(",", accountIds.Select(id => $"\"{id}\""));
        var dateStr = date.ToString("yyyy-MM-dd");
        var jql = $"worklogAuthor in ({idList}) AND worklogDate = \"{dateStr}\"";

        var keys = await SearchIssueKeysAsync(jql, ct);
        var idSet = new HashSet<string>(accountIds, StringComparer.OrdinalIgnoreCase);
        var seenWorklogIds = new HashSet<string>();

        foreach (var key in keys)
        {
            using var doc = await GetJsonAsync($"rest/api/3/issue/{key}/worklog", ct);
            if (doc is null || !doc.RootElement.TryGetProperty("worklogs", out var wls)) continue;

            foreach (var wl in wls.EnumerateArray())
            {
                var wlId = wl.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (wlId != null && !seenWorklogIds.Add(wlId)) continue;

                if (!wl.TryGetProperty("author", out var author)) continue;
                var accountId = author.TryGetProperty("accountId", out var ai) ? ai.GetString() : null;
                if (accountId is null || !idSet.Contains(accountId)) continue;

                var email = author.TryGetProperty("emailAddress", out var em) ? em.GetString() : null;
                if (string.IsNullOrEmpty(email)) email = accountId; // fall back to id as key

                var started = wl.TryGetProperty("started", out var st) ? st.GetString() : null;
                if (started is null || !TryParseJiraDate(started, out var startedDto)) continue;

                var startedLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startedDto, zone).Date);
                if (startedLocalDate != date) continue;

                var seconds = wl.TryGetProperty("timeSpentSeconds", out var ts) ? ts.GetInt32() : 0;
                result[email] = result.TryGetValue(email, out var cur) ? cur + seconds / 3600.0 : seconds / 3600.0;
            }
        }

        return result;
    }

    public async Task<List<IssueSummary>> GetActiveOrWorkloggedIssuesAsync(
        DateOnly date, string? scopeJql, CancellationToken ct = default)
    {
        var dateStr = date.ToString("yyyy-MM-dd");
        var core = $"(status = \"Active\" OR worklogDate = \"{dateStr}\")";
        var jql = string.IsNullOrWhiteSpace(scopeJql) ? core : $"({scopeJql}) AND {core}";

        var issues = new List<IssueSummary>();
        string? pageToken = null;

        do
        {
            var url = $"rest/api/3/search/jql?jql={Uri.EscapeDataString(jql)}&maxResults=100" +
                      "&fields=summary,status,assignee" +
                      (pageToken is null ? "" : $"&nextPageToken={Uri.EscapeDataString(pageToken)}");

            using var doc = await GetJsonAsync(url, ct);
            if (doc is null) break;

            if (doc.RootElement.TryGetProperty("issues", out var arr))
            {
                foreach (var issue in arr.EnumerateArray())
                {
                    var key = issue.GetProperty("key").GetString() ?? "";
                    var fields = issue.GetProperty("fields");
                    var summary = fields.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                    var status = fields.TryGetProperty("status", out var st) && st.TryGetProperty("name", out var sn)
                        ? sn.GetString() ?? "" : "";
                    string? assigneeName = null, assigneeEmail = null;
                    if (fields.TryGetProperty("assignee", out var asg) && asg.ValueKind == JsonValueKind.Object)
                    {
                        assigneeName = asg.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
                        assigneeEmail = asg.TryGetProperty("emailAddress", out var ae) ? ae.GetString() : null;
                    }
                    issues.Add(new IssueSummary(key, summary, status, assigneeName, assigneeEmail));
                }
            }

            pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var nt) ? nt.GetString() : null;
        } while (!string.IsNullOrEmpty(pageToken));

        return issues;
    }

    public async Task<List<IssueComment>> GetCommentsAsync(
        string issueKey, DateTimeOffset since, CancellationToken ct = default)
    {
        var comments = new List<IssueComment>();
        using var doc = await GetJsonAsync(
            $"rest/api/3/issue/{issueKey}/comment?orderBy=-created&maxResults=50", ct);
        if (doc is null || !doc.RootElement.TryGetProperty("comments", out var arr)) return comments;

        foreach (var c in arr.EnumerateArray())
        {
            var created = c.TryGetProperty("created", out var cr) && TryParseJiraDate(cr.GetString()!, out var dto)
                ? dto : DateTimeOffset.MinValue;
            if (created < since) continue;

            string? name = null, email = null;
            if (c.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object)
            {
                name = author.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
                email = author.TryGetProperty("emailAddress", out var ae) ? ae.GetString() : null;
            }

            var text = c.TryGetProperty("body", out var body) ? Adf.ToPlainText(body) : "";
            comments.Add(new IssueComment(name, email, created, text));
        }

        return comments;
    }

    // ---- helpers ----

    private async Task<List<string>> SearchIssueKeysAsync(string jql, CancellationToken ct)
    {
        var keys = new List<string>();
        string? pageToken = null;
        do
        {
            var url = $"rest/api/3/search/jql?jql={Uri.EscapeDataString(jql)}&maxResults=100&fields=key" +
                      (pageToken is null ? "" : $"&nextPageToken={Uri.EscapeDataString(pageToken)}");
            using var doc = await GetJsonAsync(url, ct);
            if (doc is null) break;

            if (doc.RootElement.TryGetProperty("issues", out var arr))
                foreach (var issue in arr.EnumerateArray())
                    if (issue.TryGetProperty("key", out var k) && k.GetString() is { } key)
                        keys.Add(key);

            pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var nt) ? nt.GetString() : null;
        } while (!string.IsNullOrEmpty(pageToken));

        return keys;
    }

    private async Task<JsonDocument?> GetJsonAsync(string relativeUrl, CancellationToken ct)
    {
        var jira = (await _settings.GetEffectiveAsync(ct)).Jira;
        if (string.IsNullOrWhiteSpace(jira.Url))
        {
            _log.LogWarning("Jira request skipped: no Jira URL configured (GET {Url}).", relativeUrl);
            return null;
        }

        try
        {
            var baseUri = new Uri(jira.Url.TrimEnd('/') + "/");
            using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativeUrl));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(jira.User))
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{jira.User}:{jira.ApiToken}"));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
            }

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var reason = await resp.Content.ReadAsStringAsync(ct);
                _log.LogWarning("Jira GET {Url} -> {Status}: {Body}", relativeUrl, (int)resp.StatusCode, Truncate(reason, 500));
                return null;
            }
            var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Jira GET {Url} failed", relativeUrl);
            return null;
        }
    }

    private static bool TryParseJiraDate(string value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
