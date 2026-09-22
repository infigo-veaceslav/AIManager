using AIManager.Api.Data;
using AIManager.Api.Domain;
using AIManager.Api.Services.Jira;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIManager.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private const string DefaultProjectKeys = "SUP,VENTURE";

    private readonly AppDbContext _db;
    private readonly IJiraService _jira;

    public UsersController(AppDbContext db, IJiraService jira)
    {
        _db = db;
        _jira = jira;
    }

    public record UserDto(int Id, string DisplayName, string Email, string? JiraAccountId, bool Active, string Source);
    public record CreateUserRequest(string DisplayName, string Email);
    public record UpdateUserRequest(string? DisplayName, bool? Active);
    public record SyncConfigDto(string ProjectKeys, string BoardIds);
    public record SyncResult(int Added, int Updated, int Total);

    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> Get() =>
        await _db.Users.OrderBy(u => u.DisplayName)
            .Select(u => new UserDto(u.Id, u.DisplayName, u.Email, u.JiraAccountId, u.Active, u.Source))
            .ToListAsync();

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest req)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(req.DisplayName))
            return BadRequest(new { message = "Name and email are required." });
        if (await _db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { message = "A user with that email already exists." });

        var user = new User
        {
            DisplayName = req.DisplayName.Trim(), Email = email, Active = true,
            Source = "manual", UpdatedAtUtc = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return new UserDto(user.Id, user.DisplayName, user.Email, user.JiraAccountId, user.Active, user.Source);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateUserRequest req)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();
        if (req.DisplayName is not null) user.DisplayName = req.DisplayName;
        if (req.Active is not null) user.Active = req.Active.Value;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("sync-config")]
    public async Task<ActionResult<SyncConfigDto>> GetSyncConfig()
    {
        var s = await _db.AppSettings.FirstOrDefaultAsync(x => x.Id == 1);
        return new SyncConfigDto(s?.UserSyncProjectKeys ?? DefaultProjectKeys, s?.UserSyncBoardIds ?? "");
    }

    [HttpPut("sync-config")]
    public async Task<IActionResult> PutSyncConfig(SyncConfigDto dto)
    {
        var s = await _db.AppSettings.FirstOrDefaultAsync(x => x.Id == 1);
        if (s is null) { s = new AppSettings { Id = 1 }; _db.AppSettings.Add(s); }
        s.UserSyncProjectKeys = dto.ProjectKeys;
        s.UserSyncBoardIds = dto.BoardIds;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("sync")]
    public async Task<ActionResult<SyncResult>> Sync()
    {
        var s = await _db.AppSettings.FirstOrDefaultAsync(x => x.Id == 1);
        var projects = Split(s?.UserSyncProjectKeys ?? DefaultProjectKeys);
        var boards = Split(s?.UserSyncBoardIds ?? "");

        List<JiraPerson> people;
        try { people = await _jira.GetPeopleAsync(projects, boards); }
        catch (JiraUnavailableException)
        {
            return StatusCode(503, new { message = "Jira is throttling or unavailable right now — please try again shortly." });
        }

        var result = await UpsertAsync(people.Select(p => (p.DisplayName, p.Email, p.AccountId)), "jira");
        return result;
    }

    [HttpPost("import-roster")]
    public async Task<ActionResult<SyncResult>> ImportRoster()
    {
        var members = await _db.TeamMembers
            .Select(m => new { m.DisplayName, m.Email, m.JiraAccountId }).ToListAsync();
        return await UpsertAsync(members.Select(m => (m.DisplayName, m.Email, m.JiraAccountId)), "roster");
    }

    private async Task<SyncResult> UpsertAsync(IEnumerable<(string DisplayName, string Email, string? AccountId)> people, string source)
    {
        var list = people
            .Where(p => !string.IsNullOrWhiteSpace(p.Email))
            .Select(p => (p.DisplayName, Email: p.Email.Trim().ToLowerInvariant(), p.AccountId))
            .ToList();

        var emails = list.Select(p => p.Email).Distinct().ToList();
        var existing = await _db.Users.Where(u => emails.Contains(u.Email)).ToListAsync();
        var byEmail = existing.ToDictionary(u => u.Email, StringComparer.OrdinalIgnoreCase);

        int added = 0, updated = 0;
        foreach (var p in list)
        {
            if (byEmail.TryGetValue(p.Email, out var u))
            {
                u.DisplayName = p.DisplayName;
                if (!string.IsNullOrEmpty(p.AccountId)) u.JiraAccountId = p.AccountId;
                u.UpdatedAtUtc = DateTime.UtcNow;
                updated++;
            }
            else
            {
                var nu = new User
                {
                    DisplayName = p.DisplayName, Email = p.Email, JiraAccountId = p.AccountId,
                    Active = true, Source = source, UpdatedAtUtc = DateTime.UtcNow
                };
                _db.Users.Add(nu);
                byEmail[p.Email] = nu;
                added++;
            }
        }

        await _db.SaveChangesAsync();
        return new SyncResult(added, updated, await _db.Users.CountAsync());
    }

    private static List<string> Split(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? new List<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
