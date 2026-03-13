using TwitchSharp.Api.Clients;

namespace TvGuide.Models;

/// <summary>
/// Tracked Twitch streamer with persisted live-state metadata.
/// </summary>
public sealed record TrackedTwitchUser
{
    /// <summary>
    /// Persisted Twitch user profile snapshot for the tracked streamer.
    /// </summary>
    public TrackedUserData UserData { get; set; } = new();

    /// <summary>
    /// Current live-stream snapshot when the streamer is online; otherwise <see langword="null"/>.
    /// </summary>
    public TrackedStreamData? StreamData { get; set; }

    /// <summary>
    /// Indicates whether the streamer is currently considered live.
    /// </summary>
    public bool IsLive { get; set; }

    /// <summary>
    /// UTC timestamp when the streamer was last observed online.
    /// </summary>
    public DateTimeOffset? LastOnline { get; set; }

    /// <summary>
    /// UTC timestamp when the next preview-image refresh should occur.
    /// </summary>
    public DateTimeOffset? NextMediaRefresh { get; set; }

    /// <summary>
    /// Creates a tracked-user record from Twitch API user data.
    /// </summary>
    /// <param name="userData">The Twitch user data to persist.</param>
    /// <returns>A new tracked-user record.</returns>
    public static TrackedTwitchUser Create(UserData userData) => new()
    {
        UserData = TrackedUserData.From(userData),
        StreamData = null
    };

    /// <summary>
    /// Refreshes the tracked user profile snapshot from Twitch API user data.
    /// </summary>
    /// <param name="userData">The latest Twitch user data.</param>
    public void RefreshUserData(UserData userData) => UserData = TrackedUserData.From(userData);

    /// <summary>
    /// Refreshes the tracked stream snapshot from Twitch API stream data.
    /// </summary>
    /// <param name="streamData">The latest Twitch stream data, or <see langword="null"/> when offline.</param>
    public void RefreshStreamData(StreamData? streamData) =>
        StreamData = streamData is null ? null : TrackedStreamData.From(streamData);
}

/// <summary>
/// Persisted Twitch user profile snapshot.
/// </summary>
public sealed record TrackedUserData
{
    /// <summary>
    /// User ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Login name.
    /// </summary>
    public string Login { get; set; } = string.Empty;

    /// <summary>
    /// Display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Twitch account type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Twitch broadcaster type.
    /// </summary>
    public string BroadcasterType { get; set; } = string.Empty;

    /// <summary>
    /// Channel description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Profile image URL.
    /// </summary>
    public string ProfileImageUrl { get; set; } = string.Empty;

    /// <summary>
    /// Offline image URL.
    /// </summary>
    public string OfflineImageUrl { get; set; } = string.Empty;

    /// <summary>
    /// Account creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Creates a persisted snapshot from Twitch API user data.
    /// </summary>
    /// <param name="userData">The Twitch API user data.</param>
    /// <returns>A persisted snapshot.</returns>
    public static TrackedUserData From(UserData userData)
    {
        ArgumentNullException.ThrowIfNull(userData);

        return new TrackedUserData
        {
            Id = userData.Id,
            Login = userData.Login,
            DisplayName = userData.DisplayName,
            Type = userData.Type,
            BroadcasterType = userData.BroadcasterType,
            Description = userData.Description,
            ProfileImageUrl = userData.ProfileImageUrl,
            OfflineImageUrl = userData.OfflineImageUrl,
            CreatedAt = userData.CreatedAt
        };
    }
}

/// <summary>
/// Persisted Twitch live-stream snapshot.
/// </summary>
public sealed record TrackedStreamData
{
    /// <summary>
    /// Stream ID for VOD lookup.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Broadcaster user ID.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Broadcaster login name.
    /// </summary>
    public string UserLogin { get; set; } = string.Empty;

    /// <summary>
    /// Broadcaster display name.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Category or game ID.
    /// </summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>
    /// Category or game name.
    /// </summary>
    public string GameName { get; set; } = string.Empty;

    /// <summary>
    /// Stream type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Current stream title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Custom stream tags applied to the broadcast.
    /// </summary>
    public string[] Tags { get; set; } = [];

    /// <summary>
    /// Current concurrent viewer count.
    /// </summary>
    public int ViewerCount { get; set; }

    /// <summary>
    /// When the broadcast began.
    /// </summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// Stream language.
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Thumbnail URL with <c>{width}x{height}</c> placeholders.
    /// </summary>
    public string ThumbnailUrl { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the stream is marked as mature content.
    /// </summary>
    public bool IsMature { get; set; }

    /// <summary>
    /// Creates a persisted snapshot from Twitch API stream data.
    /// </summary>
    /// <param name="streamData">The Twitch API stream data.</param>
    /// <returns>A persisted stream snapshot.</returns>
    public static TrackedStreamData From(StreamData streamData)
    {
        ArgumentNullException.ThrowIfNull(streamData);

        return new TrackedStreamData
        {
            Id = streamData.Id,
            UserId = streamData.UserId,
            UserLogin = streamData.UserLogin,
            UserName = streamData.UserName,
            GameId = streamData.GameId,
            GameName = streamData.GameName,
            Type = streamData.Type,
            Title = streamData.Title,
            Tags = [.. streamData.Tags],
            ViewerCount = streamData.ViewerCount,
            StartedAt = streamData.StartedAt,
            Language = streamData.Language,
            ThumbnailUrl = streamData.ThumbnailUrl,
            IsMature = streamData.IsMature
        };
    }
}