using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TvGuide.Modules;

public sealed class UsersModule(
    HttpClient httpClient,
    IAuthenticationModule authService,
    IOptions<Configuration> settings,
    ILogger<UsersModule> logger)
    : IUsersModule
{
    private readonly IAuthenticationModule _authService = authService;
    private readonly Settings.Twitch _settings = settings.Value.Twitch;
    private readonly ILogger<UsersModule> _logger = logger;
    private readonly HttpClient _httpClient = httpClient;

    public async Task<TwitchUser?> GetUserAsync(string userLogin, CancellationToken cancellationToken = default)
    {
        var token = await _authService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, GetBaseUrl($"users?login={userLogin}"));
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("Client-Id", _settings.Authentication.ClientId);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (_logger.IsEnabled(LogLevel.Error))
            {
                _logger.LogError(
                    "Failed to get user info: {StatusCode} - {Content}",
                    response.StatusCode,
                    errorContent);
            }

            return null;
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync<TwitchUserResponse>(
            responseStream,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result?.Data.Count > 0 ? result.Data[0] : null;
    }

    private static string GetBaseUrl(string endpoint) => $"https://api.twitch.tv/helix/{endpoint}";
}

/// <summary>
/// Twitch users API response.
/// </summary>
public sealed record TwitchUserResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<TwitchUser> Data);

/// <summary>
/// Twitch user profile.
/// </summary>
public sealed record TwitchUser
{
    /// <summary>
    /// User ID.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Login name.
    /// </summary>
    [JsonPropertyName("login")]
    public required string Login { get; init; }

    /// <summary>
    /// Display name.
    /// </summary>
    [JsonPropertyName("display_name")]
    public required string DisplayName { get; init; }

    /// <summary>
    /// The type of user. Possible values are:
    /// - admin: Twitch administrator
    /// - global_mod: Twitch global moderator
    /// - staff: Twitch staff
    /// - "": Normal user
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// The type of broadcaster. Possible values are:
    /// - affiliate: An affiliate broadcaster
    /// - partner: A partner broadcaster
    /// - "": A normal broadcaster
    /// </summary>
    [JsonPropertyName("broadcaster_type")]
    public required string BroadcasterType { get; init; }

    /// <summary>
    /// The user's description of their channel.
    /// </summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>
    /// The profile icon of the user.
    /// </summary>
    [JsonPropertyName("profile_image_url")]
    public required string ProfileImageUrl { get; init; }

    /// <summary>
    /// A Url to the user's offline image. Usually has a resolution of 1920x1080.
    /// </summary>
    [JsonPropertyName("offline_image_url")]
    public required string OfflineImageUrl { get; init; }

    /// <summary>
    /// The UTC date and time that the user's account was created. The timestamp is in RFC3339 format.
    /// </summary>
    [JsonPropertyName("created_at")]
    public required DateTime CreatedAt { get; init; }
}

/// <summary>
/// User management operation result.
/// </summary>
public enum UserManagementResult
{
    NotFound = 0,
    Success = 1,
    AlreadyExists = 2,
    Error = 3
}

/// <summary>
/// Tracked Twitch streamer with status.
/// </summary>
public sealed record TwitchStreamer
{
    public required TwitchUser UserData { get; set; }
    public required TwitchStream? StreamData { get; set; }
    public bool IsLive { get; set; }
    public DateTime? LastOnline { get; set; }
    public DateTime? NextMediaRefresh { get; set; }
}