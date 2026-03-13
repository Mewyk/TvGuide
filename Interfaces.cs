using TvGuide.Models;

namespace TvGuide;

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
