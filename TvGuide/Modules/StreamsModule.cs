using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using TvGuide.Twitch;

namespace TvGuide.Modules;

public class StreamsModule(
    HttpClient httpClient,
    IAuthenticationModule authService,
    IOptions<Configuration> settings,
    ILogger<StreamsModule> logger)
    : IStreamsModule
{
    private readonly IAuthenticationModule _authService = authService;
    private readonly Settings.Twitch _settings = settings.Value.Twitch;
    private readonly ILogger<StreamsModule> _logger = logger;
    private readonly HttpClient _httpClient = httpClient;

    public async Task<(IReadOnlyList<TwitchStream> Streams, string? NextCursor)> GetStreamsAsync(
        TwitchStreamRequest parameters,
        CancellationToken cancellationToken = default)
    {
        ValidateParameters(parameters);
        var token = await _authService.GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, GetBaseUrl($"streams?{parameters.ToQueryString()}"));
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("Client-Id", _settings.Authentication.ClientId);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception(
                $"Twitch API request failed: {response.StatusCode} - {await response.Content.ReadAsStringAsync(cancellationToken)}");

        var result = await JsonSerializer.DeserializeAsync<TwitchStreamsResponse>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        return result is null 
            ? ((IReadOnlyList<TwitchStream> Streams, string? NextCursor))([], null)
            : ((IReadOnlyList<TwitchStream> Streams, string? NextCursor))(result.Data, result.Pagination?.Cursor);
    }

    public async IAsyncEnumerable<TwitchStream> GetAllStreamsAsync(
        TwitchStreamRequest parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var requestParams = new TwitchStreamRequest
            {
                UserIds = parameters.UserIds,
                UserLogins = parameters.UserLogins,
                GameIds = parameters.GameIds,
                Type = parameters.Type,
                Languages = parameters.Languages,
                First = parameters.First,
                After = cursor
            };

            var (streams, nextCursor) = await GetStreamsAsync(requestParams, cancellationToken);

            foreach (var stream in streams)
                yield return stream;

            cursor = nextCursor;
        }
        while (cursor != null);
    }

    private static void ValidateParameters(TwitchStreamRequest parameters)
    {
        if (parameters.First is < 1 or > 100)
            throw new ArgumentException("First must be between 1 and 100");

        var maxCountExceeded = new[]
        {
            parameters.UserIds?.Count,
            parameters.UserLogins?.Count,
            parameters.GameIds?.Count,
            parameters.Languages?.Count
        }
        .Any(count => count > 100);

        if (maxCountExceeded)
            throw new ArgumentException("Maximum of 100 Id/login/language allowed per request");

        if (!string.IsNullOrEmpty(parameters.Type) && parameters.Type is not ("all" or "live"))
            throw new ArgumentException("Type must be either 'all' or 'live'");
    }

    // TODO: Do this properly
    private static string GetBaseUrl(string endpoint) => $"https://api.twitch.tv/helix/{endpoint}";
}

public class TwitchStreamRequest
{
    public IReadOnlyList<string>? UserIds { get; init; }
    public IReadOnlyList<string>? UserLogins { get; init; }
    public IReadOnlyList<string>? GameIds { get; init; }
    public string? Type { get; init; }
    public IReadOnlyList<string>? Languages { get; init; }
    public int First { get; init; } = 20;
    public string? Before { get; init; }
    public string? After { get; init; }

    public string ToQueryString()
    {
        var parameters = new List<string>();

        if (UserIds != null)
            parameters.AddRange(UserIds.Select(id => $"user_id={id}"));
        if (UserLogins != null)
            parameters.AddRange(UserLogins.Select(login => $"user_login={login}"));
        if (GameIds != null)
            parameters.AddRange(GameIds.Select(id => $"game_id={id}"));
        if (!string.IsNullOrEmpty(Type))
            parameters.Add($"type={Type}");
        if (Languages != null)
            parameters.AddRange(Languages.Select(lang => $"language={lang}"));
        if (First != 20)
            parameters.Add($"first={First}");
        if (!string.IsNullOrEmpty(Before))
            parameters.Add($"before={Before}");
        if (!string.IsNullOrEmpty(After))
            parameters.Add($"after={After}");

        return string.Join("&", parameters);
    }
}

public class TwitchStreamsResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<TwitchStream> Data { get; init; }

    [JsonPropertyName("pagination")]
    public Pagination? Pagination { get; init; }
}

/// <summary>
/// Represents a stream in the Twitch API.
/// </summary>
public class TwitchStream
{
    /// <summary>
    /// An ID that identifies the stream. You can use this ID later to look up the video on demand (VOD).
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// The ID of the user that’s broadcasting the stream.
    /// </summary>
    [JsonPropertyName("user_id")]
    public required string UserId { get; init; }

    /// <summary>
    /// The user’s login name.
    /// </summary>
    [JsonPropertyName("user_login")]
    public required string UserLogin { get; init; }

    /// <summary>
    /// The user’s display name.
    /// </summary>
    [JsonPropertyName("user_name")]
    public required string UserName { get; init; }

    /// <summary>
    /// The ID of the category or game being played.
    /// </summary>
    [JsonPropertyName("game_id")]
    public required string GameId { get; init; }

    /// <summary>
    /// The name of the category or game being played.
    /// </summary>
    [JsonPropertyName("game_name")]
    public required string GameName { get; init; }

    /// <summary>
    /// The type of stream. Possible values are:
    /// - live
    /// If an error occurs, this field is set to an empty string.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// The stream’s title. Is an empty string if not set.
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// The tags applied to the stream.
    /// </summary>
    [JsonPropertyName("tags")]
    public required string[] Tags { get; init; }

    /// <summary>
    /// The number of users watching the stream.
    /// </summary>
    [JsonPropertyName("viewer_count")]
    public required int ViewerCount { get; init; }

    /// <summary>
    /// The UTC date and time (in RFC3339 format) of when the broadcast began.
    /// </summary>
    [JsonPropertyName("started_at")]
    public required DateTime StartedAt { get; init; }

    /// <summary>
    /// The language that the stream uses. This is an ISO 639-1 two-letter language code or other if the stream uses a language not in the list of supported stream languages.
    /// </summary>
    [JsonPropertyName("language")]
    public required string Language { get; init; }

    /// <summary>
    /// A URL to an image of a frame from the last 5 minutes of the stream. Replace the width and height placeholders in the URL ({width}x{height}) with the size of the image you want, in pixels.
    /// </summary>
    [JsonPropertyName("thumbnail_url")]
    public required string ThumbnailUrl { get; init; }

    /// <summary>
    /// A Boolean value that indicates whether the stream is meant for mature audiences.
    /// </summary>
    [JsonPropertyName("is_mature")]
    public required bool IsMature { get; init; }
}