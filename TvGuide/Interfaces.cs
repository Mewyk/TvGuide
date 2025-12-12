using TvGuide.Modules;

namespace TvGuide;

/// <summary>
/// Provides Twitch API authentication and token management.
/// </summary>
public interface IAuthenticationModule
{
    /// <summary>
    /// Gets a valid access token, refreshing if necessary.
    /// </summary>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides Twitch user information lookups.
/// </summary>
public interface IUsersModule
{
    /// <summary>
    /// Gets user by login name, or null if not found.
    /// </summary>
    Task<TwitchUser?> GetUserAsync(string userLogin, CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides Twitch stream information with pagination.
/// </summary>
public interface IStreamsModule
{
    /// <summary>
    /// Gets a page of streams (streams, next cursor).
    /// </summary>
    Task<(IReadOnlyList<TwitchStream> Streams, string? NextCursor)> GetStreamsAsync(
        TwitchStreamRequest parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all streams, automatically handling pagination.
    /// </summary>
    IAsyncEnumerable<TwitchStream> GetAllStreamsAsync(
        TwitchStreamRequest parameters,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Manages tracked users for live notifications.
/// </summary>
public interface INowLiveService
{
    /// <summary>
    /// Adds a user to tracking.
    /// </summary>
    Task<UserManagementResult> AddUserAsync(string userLogin, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Removes a user from tracking.
    /// </summary>
    Task<UserManagementResult> RemoveUserAsync(string userLogin, CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides Twitch clip information with pagination.
/// </summary>
public interface IClipsModule
{
    /// <summary>
    /// Gets a page of clips (clips, next cursor).
    /// </summary>
    Task<(IReadOnlyList<TwitchClip> Clips, string? NextCursor)> GetClipsAsync(
        TwitchClipRequest parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all clips, automatically handling pagination.
    /// </summary>
    IAsyncEnumerable<TwitchClip> GetAllClipsAsync(
        TwitchClipRequest parameters,
        CancellationToken cancellationToken = default);
}
