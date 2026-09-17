using AIManager.Api.Data;
using AIManager.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIManager.Api.Controllers;

[ApiController]
[Route("api")]
public class TeamsController : ControllerBase
{
    private readonly AppDbContext _db;
    public TeamsController(AppDbContext db) => _db = db;

    public record MemberDto(int Id, string DisplayName, string Email, string? JiraAccountId, bool Active);
    public record TeamDto(int Id, string Name, string Timezone, bool Enabled, List<MemberDto> Members);
    public record CreateMemberRequest(string DisplayName, string Email);
    public record UpdateMemberRequest(string? DisplayName, bool? Active, string? JiraAccountId);

    [HttpGet("teams")]
    public async Task<ActionResult<List<TeamDto>>> GetTeams()
    {
        var teams = await _db.Teams
            .Include(t => t.Members)
            .Select(t => new TeamDto(t.Id, t.Name, t.Timezone, t.Enabled,
                t.Members.OrderBy(m => m.DisplayName)
                    .Select(m => new MemberDto(m.Id, m.DisplayName, m.Email, m.JiraAccountId, m.Active))
                    .ToList()))
            .ToListAsync();
        return teams;
    }

    [HttpPost("teams/{teamId:int}/members")]
    public async Task<ActionResult<MemberDto>> AddMember(int teamId, CreateMemberRequest req)
    {
        if (!await _db.Teams.AnyAsync(t => t.Id == teamId)) return NotFound();
        var member = new TeamMember { TeamId = teamId, DisplayName = req.DisplayName, Email = req.Email, Active = true };
        _db.TeamMembers.Add(member);
        await _db.SaveChangesAsync();
        return new MemberDto(member.Id, member.DisplayName, member.Email, member.JiraAccountId, member.Active);
    }

    [HttpPut("members/{id:int}")]
    public async Task<IActionResult> UpdateMember(int id, UpdateMemberRequest req)
    {
        var member = await _db.TeamMembers.FindAsync(id);
        if (member is null) return NotFound();
        if (req.DisplayName is not null) member.DisplayName = req.DisplayName;
        if (req.Active is not null) member.Active = req.Active.Value;
        if (req.JiraAccountId is not null) member.JiraAccountId = req.JiraAccountId;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("members/{id:int}")]
    public async Task<IActionResult> DeleteMember(int id)
    {
        var member = await _db.TeamMembers.FindAsync(id);
        if (member is null) return NotFound();
        _db.TeamMembers.Remove(member);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
