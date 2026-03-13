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
    /// <param name="cancellationToken">Cancellation token to cancel token acquisition.</param>
    /// <returns>A valid Twitch OAuth access token.</returns>
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
    /// <param name="userLogin">Twitch login name to look up.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the lookup.</param>
    /// <returns>The matching <see cref="TwitchUser"/>, or <see langword="null"/> when not found.</returns>
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
    /// <param name="parameters">Request parameters used to filter and paginate the query.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the request.</param>
    /// <returns>A page of streams and the cursor for the next page, if any.</returns>
    Task<(IReadOnlyList<TwitchBroadcast> Streams, string? NextCursor)> GetStreamsAsync(
        TwitchStreamRequest parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all streams, automatically handling pagination.
    /// </summary>
    /// <param name="parameters">Request parameters used to filter the query.</param>
    /// <param name="cancellationToken">Cancellation token to cancel enumeration.</param>
    /// <returns>An async sequence of all matching streams.</returns>
    IAsyncEnumerable<TwitchBroadcast> GetAllStreamsAsync(
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
    /// <param name="userLogin">Twitch login name to add.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the add operation.</param>
    /// <returns>The outcome of the add operation.</returns>
    Task<UserManagementResult> AddUserAsync(string userLogin, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Removes a user from tracking.
    /// </summary>
    /// <param name="userLogin">Twitch login name to remove.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the remove operation.</param>
    /// <returns>The outcome of the remove operation.</returns>
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
    /// <param name="parameters">Request parameters used to filter and paginate the query.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the request.</param>
    /// <returns>A page of clips and the cursor for the next page, if any.</returns>
    Task<(IReadOnlyList<TwitchClip> Clips, string? NextCursor)> GetClipsAsync(
        TwitchClipRequest parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all clips, automatically handling pagination.
    /// </summary>
    /// <param name="parameters">Request parameters used to filter the query.</param>
    /// <param name="cancellationToken">Cancellation token to cancel enumeration.</param>
    /// <returns>An async sequence of all matching clips.</returns>
    IAsyncEnumerable<TwitchClip> GetAllClipsAsync(
        TwitchClipRequest parameters,
        CancellationToken cancellationToken = default);
}
