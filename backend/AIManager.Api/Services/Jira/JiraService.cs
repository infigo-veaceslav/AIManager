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

    public async Task<List<SupportIssue>> GetSupportIssuesAsync(
        string projectKey, IReadOnlyCollection<string> statuses, IReadOnlyCollection<string> reporterEmails,
        int maxIssues, CancellationToken ct = default)
    {
        var statusList = string.Join(",", statuses.Select(s => $"\"{s}\""));
        var jql = $"project = {projectKey} AND status in ({statusList}) ORDER BY updated DESC";

        var issues = new List<SupportIssue>();
        string? pageToken = null;
        do
        {
            var url = $"rest/api/3/search/jql?jql={Uri.EscapeDataString(jql)}&maxResults=100" +
                      "&fields=summary,status,assignee,reporter" +
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
                    var (assigneeName, assigneeEmail) = ReadUser(fields, "assignee");
                    var (reporterName, reporterEmail) = ReadUser(fields, "reporter");
                    issues.Add(new SupportIssue(key, summary, status, assigneeName, assigneeEmail, reporterName, reporterEmail));
                }
            }

            pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var nt) ? nt.GetString() : null;
            if (maxIssues > 0 && issues.Count >= maxIssues) break;
        } while (!string.IsNullOrEmpty(pageToken));

        if (reporterEmails.Count > 0)
        {
            var set = new HashSet<string>(reporterEmails, StringComparer.OrdinalIgnoreCase);
            issues = issues.Where(i => i.ReporterEmail is not null && set.Contains(i.ReporterEmail)).ToList();
        }
        if (maxIssues > 0 && issues.Count > maxIssues)
            issues = issues.Take(maxIssues).ToList();

        return issues;
    }

    private const int PeopleMaxPages = 10; // ~1000 recent issues per source

    public async Task<List<JiraPerson>> GetPeopleAsync(
        IReadOnlyCollection<string> projectKeys, IReadOnlyCollection<string> boardIds, CancellationToken ct = default)
    {
        var byKey = new Dictionary<string, JiraPerson>(StringComparer.OrdinalIgnoreCase);

        void Collect(JsonElement issue)
        {
            if (!issue.TryGetProperty("fields", out var fields)) return;
            foreach (var prop in new[] { "assignee", "reporter" })
            {
                if (!fields.TryGetProperty(prop, out var u) || u.ValueKind != JsonValueKind.Object) continue;
                var email = u.TryGetProperty("emailAddress", out var em) ? em.GetString() : null;
                if (string.IsNullOrEmpty(email)) continue;
                var accountId = u.TryGetProperty("accountId", out var a) ? a.GetString() : null;
                var name = u.TryGetProperty("displayName", out var dn) ? dn.GetString() : email;
                byKey.TryAdd(accountId ?? email, new JiraPerson(accountId, name ?? email, email));
            }
        }

        // Projects — recent issues via the enhanced search endpoint.
        foreach (var project in projectKeys)
        {
            var jql = $"project = {project} ORDER BY updated DESC";
            string? pageToken = null;
            var pages = 0;
            do
            {
                var url = $"rest/api/3/search/jql?jql={Uri.EscapeDataString(jql)}&maxResults=100&fields=assignee,reporter" +
                          (pageToken is null ? "" : $"&nextPageToken={Uri.EscapeDataString(pageToken)}");
                using var doc = await GetJsonAsync(url, ct);
                if (doc is null) break;
                if (doc.RootElement.TryGetProperty("issues", out var arr))
                    foreach (var issue in arr.EnumerateArray()) Collect(issue);
                pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var nt) ? nt.GetString() : null;
            } while (!string.IsNullOrEmpty(pageToken) && ++pages < PeopleMaxPages);
        }

        // Boards — agile API (startAt/total paging).
        foreach (var boardId in boardIds)
        {
            var startAt = 0;
            var pages = 0;
            while (pages++ < PeopleMaxPages)
            {
                var url = $"rest/agile/1.0/board/{Uri.EscapeDataString(boardId)}/issue?fields=assignee,reporter&maxResults=100&startAt={startAt}";
                using var doc = await GetJsonAsync(url, ct);
                if (doc is null || !doc.RootElement.TryGetProperty("issues", out var arr)) break;

                var count = 0;
                foreach (var issue in arr.EnumerateArray()) { Collect(issue); count++; }
                var total = doc.RootElement.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
                startAt += 100;
                if (count == 0 || startAt >= total) break;
            }
        }

        return byKey.Values.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (string? Name, string? Email) ReadUser(JsonElement fields, string property)
    {
        if (fields.TryGetProperty(property, out var u) && u.ValueKind == JsonValueKind.Object)
        {
            var name = u.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
            var email = u.TryGetProperty("emailAddress", out var ae) ? ae.GetString() : null;
            return (name, email);
        }
        return (null, null);
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

    private const int MaxAttempts = 4;
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// GETs and parses JSON, with retry on 429/5xx honoring Retry-After. Returns null for a
    /// non-transient "no data" response (e.g. 4xx). Throws <see cref="JiraUnavailableException"/>
    /// when throttling/failures persist past <see cref="MaxAttempts"/> so callers can fail safe.
    /// </summary>
    private async Task<JsonDocument?> GetJsonAsync(string relativeUrl, CancellationToken ct)
    {
        var jira = (await _settings.GetEffectiveAsync(ct)).Jira;
        if (string.IsNullOrWhiteSpace(jira.Url))
        {
            _log.LogWarning("Jira request skipped: no Jira URL configured (GET {Url}).", relativeUrl);
            return null;
        }

        var baseUri = new Uri(jira.Url.TrimEnd('/') + "/");
        string? auth = string.IsNullOrWhiteSpace(jira.User)
            ? null
            : Convert.ToBase64String(Encoding.UTF8.GetBytes($"{jira.User}:{jira.ApiToken}"));

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativeUrl));
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (auth is not null)
                    req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);

                var resp = await _http.SendAsync(req, ct);
                var status = (int)resp.StatusCode;

                if (resp.IsSuccessStatusCode)
                {
                    await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                    var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                    resp.Dispose();
                    return doc;
                }

                var transient = status == 429 || status is >= 500 and <= 599;
                if (transient && attempt < MaxAttempts)
                {
                    var delay = ComputeDelay(resp, attempt);
                    resp.Dispose();
                    _log.LogWarning("Jira GET {Url} -> {Status}; retry {Attempt}/{Max} in {Delay:0.#}s.",
                        relativeUrl, status, attempt, MaxAttempts, delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                    continue;
                }

                var body = await resp.Content.ReadAsStringAsync(ct);
                resp.Dispose();

                if (transient)
                {
                    _log.LogError("Jira GET {Url} still {Status} after {Max} attempts — treating as unavailable.",
                        relativeUrl, status, MaxAttempts);
                    throw new JiraUnavailableException($"Jira returned {status} for '{relativeUrl}' after {MaxAttempts} attempts.");
                }

                // Non-transient (e.g. 400/401/404): genuine "no data", handled gracefully by callers.
                _log.LogWarning("Jira GET {Url} -> {Status}: {Body}", relativeUrl, status, Truncate(body, 500));
                return null;
            }
            catch (JiraUnavailableException) { throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                var delay = Backoff(attempt);
                _log.LogWarning(ex, "Jira GET {Url} network error (attempt {Attempt}/{Max}); retry in {Delay:0.#}s.",
                    relativeUrl, attempt, MaxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Jira GET {Url} failed after {Max} attempts.", relativeUrl, MaxAttempts);
                throw new JiraUnavailableException($"Jira request to '{relativeUrl}' failed after {MaxAttempts} attempts.", ex);
            }
        }
    }

    /// <summary>Delay for a transient response: honor Retry-After, else exponential backoff with jitter.</summary>
    private static TimeSpan ComputeDelay(HttpResponseMessage resp, int attempt)
    {
        var ra = resp.Headers.RetryAfter;
        if (ra is not null)
        {
            if (ra.Delta is { } delta && delta > TimeSpan.Zero) return Cap(delta);
            if (ra.Date is { } date)
            {
                var wait = date - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero) return Cap(wait);
            }
        }
        return Backoff(attempt);
    }

    private static TimeSpan Backoff(int attempt)
    {
        var seconds = Math.Pow(2, attempt - 1); // 1, 2, 4, …
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
        return Cap(TimeSpan.FromSeconds(seconds) + jitter);
    }

    private static TimeSpan Cap(TimeSpan t) => t > MaxDelay ? MaxDelay : t;

    private static bool TryParseJiraDate(string value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
