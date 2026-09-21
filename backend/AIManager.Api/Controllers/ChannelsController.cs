using AIManager.Api.Data;
using AIManager.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIManager.Api.Controllers;

[ApiController]
[Route("api/channels")]
public class ChannelsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ChannelsController(AppDbContext db) => _db = db;

    public record ChannelDto(int Id, string Name, string TeamId, string ChannelId);
    public record CreateChannelRequest(string Name, string TeamId, string ChannelId);
    public record UpdateChannelRequest(string? Name, string? TeamId, string? ChannelId);

    [HttpGet]
    public async Task<ActionResult<List<ChannelDto>>> Get() =>
        await _db.TeamsChannels.OrderBy(c => c.Name)
            .Select(c => new ChannelDto(c.Id, c.Name, c.TeamId, c.ChannelId)).ToListAsync();

    [HttpPost]
    public async Task<ActionResult<ChannelDto>> Create(CreateChannelRequest req)
    {
        var c = new TeamsChannel
        {
            Name = req.Name,
            TeamId = req.TeamId,
            ChannelId = req.ChannelId,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.TeamsChannels.Add(c);
        await _db.SaveChangesAsync();
        return new ChannelDto(c.Id, c.Name, c.TeamId, c.ChannelId);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateChannelRequest req)
    {
        var c = await _db.TeamsChannels.FindAsync(id);
        if (c is null) return NotFound();
        if (req.Name is not null) c.Name = req.Name;
        if (req.TeamId is not null) c.TeamId = req.TeamId;
        if (req.ChannelId is not null) c.ChannelId = req.ChannelId;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var c = await _db.TeamsChannels.FindAsync(id);
        if (c is null) return NotFound();
        _db.TeamsChannels.Remove(c);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
