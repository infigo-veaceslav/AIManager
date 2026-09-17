namespace AIManager.Api.Services.Teams;

public interface ITeamsService
{
    /// <summary>
    /// Sends a 1:1 Teams DM (as the operator) by POSTing to the Power Automate flow.
    /// Returns true on success. Never throws — failures are logged and reported as false.
    /// </summary>
    Task<bool> SendDmAsync(string recipientEmail, string htmlMessage, CancellationToken ct = default);
}
