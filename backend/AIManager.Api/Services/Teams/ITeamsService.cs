namespace AIManager.Api.Services.Teams;

/// <summary>A person to @mention: index matches an &lt;at id="{Index}"&gt; tag in the message content.</summary>
public record MentionTarget(int Index, string Email, string DisplayName);

public interface ITeamsService
{
    /// <summary>
    /// Sends a 1:1 Teams DM (as the operator) via the Power Automate flow.
    /// Returns true on success. Never throws — failures are logged and reported as false.
    /// </summary>
    Task<bool> SendDmAsync(string recipientEmail, string htmlMessage, CancellationToken ct = default);

    /// <summary>Posts a message to a Teams channel via the same Power Automate flow.</summary>
    Task<bool> SendChannelAsync(string teamId, string channelId, string htmlMessage, CancellationToken ct = default);

    /// <summary>
    /// Posts a channel message that @mentions people. The sender resolves each email to an Azure AD id
    /// and builds the Graph mentions array; content must contain matching &lt;at id="{index}"&gt; tags.
    /// </summary>
    Task<bool> SendChannelWithMentionsAsync(
        string teamId, string channelId, string htmlMessage, IReadOnlyList<MentionTarget> people,
        CancellationToken ct = default);
}
