using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TvGuide.Modules;

public class UsersModule(
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
        var token = await _authService.GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, GetBaseUrl($"users?login={userLogin}"));
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("Client-Id", _settings.Authentication.ClientId);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Failed to get user info: {StatusCode} - {Content}",
                response.StatusCode,
                errorContent);

            return null;
        }

        var result = await JsonSerializer.DeserializeAsync<TwitchUserResponse>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        return result?.Data.Count > 0 ? result.Data[0] : null;
    }

    // TODO: Do this properly
    private static string GetBaseUrl(string endpoint) => $"https://api.twitch.tv/helix/{endpoint}";
}

/// <summary>
/// Represents a Twitch user.
/// </summary>
public class TwitchUserResponse
{
    /// <summary>
    /// The list of users.
    /// </summary>
    [JsonPropertyName("data")]
    public required IReadOnlyList<TwitchUser> Data { get; init; }
}

/// <summary>
/// Represents a user in the Twitch Api.
/// </summary>
public class TwitchUser
{
    /// <summary>
    /// An ID that identifies the user.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// The user’s login name.
    /// </summary>
    [JsonPropertyName("login")]
    public required string Login { get; init; }

    /// <summary>
    /// The user’s display name.
    /// </summary>
    [JsonPropertyName("display_name")]
    public required string DisplayName { get; init; }

    /// <summary>
    /// The type of user. Possible values are:
    /// - admin — Twitch administrator
    /// - global_mod
    /// - staff — Twitch staff
    /// - "" — Normal user
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// The type of broadcaster. Possible values are:
    /// - affiliate — An affiliate broadcaster
    /// - partner — A partner broadcaster
    /// - "" — A normal broadcaster
    /// </summary>
    [JsonPropertyName("broadcaster_type")]
    public required string BroadcasterType { get; init; }

    /// <summary>
    /// The user’s description of their channel.
    /// </summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>
    /// The profile icon of the user.
    /// </summary>
    [JsonPropertyName("profile_image_url")]
    public required string ProfileImageUrl { get; init; }

    /// <summary>
    /// A Url to the user’s offline image. Usually has a resolution of 1920x1080.
    /// </summary>
    [JsonPropertyName("offline_image_url")]
    public required string OfflineImageUrl { get; init; }

    /// <summary>
    /// The UTC date and time that the user’s account was created. The timestamp is in RFC3339 format.
    /// </summary>
    [JsonPropertyName("created_at")]
    public required DateTime CreatedAt { get; init; }
}

public enum UserManagementResult
{
    NotFound = 0,
    Success = 1,
    AlreadyExists = 2,
    Error = 3
}

public class TwitchStreamer
{
    public required TwitchUser UserData { get; set; }
    public required TwitchStream? StreamData { get; set; }
    public bool IsLive { get; set; }
    public DateTime? LastOnline { get; set; }
    public DateTime? NextMediaRefresh { get; set; }
}