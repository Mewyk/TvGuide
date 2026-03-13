namespace TvGuide.Models;

/// <summary>
/// Active broadcasts tracked in a single Discord message.
/// </summary>
/// <remarks>
/// Discord Components V2 limits: 10 containers max, 40 total components max per message.
/// Each broadcast = 1 container. Summary = additional components (not containerized).
/// </remarks>
public sealed class ActiveBroadcastsState
{
    /// <summary>
    /// Discord message ID containing all active broadcasts.
    /// </summary>
    public ulong ActiveBroadcastsMessageId { get; set; }

    /// <summary>
    /// Active broadcast data.
    /// </summary>
    public List<ActiveBroadcastEntry> ActiveBroadcasts { get; set; } = [];
}

/// <summary>
/// Persisted broadcast data for a single tracked streamer.
/// </summary>
public sealed class ActiveBroadcastEntry
{
    /// <summary>
    /// Persisted Twitch user profile information for the broadcast.
    /// </summary>
    public TrackedUserData UserData { get; set; } = new();

    /// <summary>
    /// Current live-stream data when the user is online; otherwise <see langword="null"/>.
    /// </summary>
    public TrackedStreamData? StreamData { get; set; }

    /// <summary>
    /// UTC timestamp when this broadcast entry was last refreshed.
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}