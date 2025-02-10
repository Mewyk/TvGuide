using TvGuide.Modules;

namespace TvGuide;

public interface IAuthenticationModule
{
    Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken = default);
}

public interface IUsersModule
{
    Task<TwitchUser?> GetUserAsync(
        string userLogin, 
        CancellationToken cancellationToken = default);
}

public interface IStreamsModule
{
    Task<(IReadOnlyList<TwitchStream> Streams, string? NextCursor)> GetStreamsAsync(
        TwitchStreamRequest parameters,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<TwitchStream> GetAllStreamsAsync(
        TwitchStreamRequest parameters,
        CancellationToken cancellationToken = default);
}

public interface INowStreamingService
{
    Task<UserManagementResult> AddUserAsync(
        string userLogin, 
        CancellationToken cancellationToken = default);

    Task<UserManagementResult> RemoveUserAsync(
        string userLogin, 
        CancellationToken cancellationToken = default);
}

public interface IClipsModule
{
    Task<(IReadOnlyList<TwitchClip> Clips, string? NextCursor)> GetClipsAsync(
        TwitchClipRequest parameters,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<TwitchClip> GetAllClipsAsync(
        TwitchClipRequest parameters,
        CancellationToken cancellationToken = default);
}
