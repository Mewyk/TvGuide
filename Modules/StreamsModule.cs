using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TvGuide.Twitch;

namespace TvGuide.Modules;

/// <summary>
/// Retrieves live-stream data from the Twitch Helix streams endpoint.
/// </summary>
public sealed class StreamsModule(
    HttpClient httpClient,
    IAuthenticationModule authService,
    IOptions<Configuration> settings)
    : IStreamsModule
{
    private readonly IAuthenticationModule _authService = authService;
    private readonly Settings.Twitch _settings = settings.Value.Twitch;
    private readonly HttpClient _httpClient = httpClient;

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<TwitchBroadcast> Streams, string? NextCursor)> GetStreamsAsync(
        TwitchStreamRequest parameters,
        CancellationToken cancellationToken = default)
    {
        ValidateParameters(parameters);
        var token = await _authService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, GetBaseUrl($"streams?{parameters.ToQueryString()}"));
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("Client-Id", _settings.Authentication.ClientId);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Twitch API request failed: {response.StatusCode} - {errorContent}",
                null,
                response.StatusCode);
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync<TwitchStreamsResponse>(
            responseStream,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result is null 
            ? ([], null)
            : (result.Data, result.Pagination?.Cursor);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TwitchBroadcast> GetAllStreamsAsync(
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

        if (parameters.Type is { Length: > 0 } and not ("all" or "live"))
            throw new ArgumentException("Type must be either 'all' or 'live'");
    }

    private static string GetBaseUrl(string endpoint) => $"https://api.twitch.tv/helix/{endpoint}";
}

/// <summary>
/// Twitch streams API request parameters.
/// </summary>
public sealed record TwitchStreamRequest
{
    /// <summary>
    /// Broadcaster user IDs to query. Maximum 100 values.
    /// </summary>
    public IReadOnlyList<string>? UserIds { get; init; }

    /// <summary>
    /// Broadcaster login names to query. Maximum 100 values.
    /// </summary>
    public IReadOnlyList<string>? UserLogins { get; init; }

    /// <summary>
    /// Category or game IDs to filter by. Maximum 100 values.
    /// </summary>
    public IReadOnlyList<string>? GameIds { get; init; }

    /// <summary>
    /// Stream type filter. Supported values are <c>all</c> and <c>live</c>.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// Stream languages to filter by. Maximum 100 values.
    /// </summary>
    public IReadOnlyList<string>? Languages { get; init; }

    /// <summary>
    /// Maximum number of results to return per page. Valid range: 1 to 100.
    /// </summary>
    public int First { get; init; } = 20;

    /// <summary>
    /// Pagination cursor for retrieving the previous page.
    /// </summary>
    public string? Before { get; init; }

    /// <summary>
    /// Pagination cursor for retrieving the next page.
    /// </summary>
    public string? After { get; init; }

    /// <summary>
    /// Converts the request parameters into a Twitch Helix query string.
    /// </summary>
    /// <returns>A URL query string without a leading question mark.</returns>
    public string ToQueryString()
    {
        var parameters = new List<string>();

        if (UserIds != null)
            parameters.AddRange(UserIds.Select(id => $"user_id={id}"));
        if (UserLogins != null)
            parameters.AddRange(UserLogins.Select(login => $"user_login={login}"));
        if (GameIds != null)
            parameters.AddRange(GameIds.Select(id => $"game_id={id}"));
        if (Type is { Length: > 0 })
            parameters.Add($"type={Type}");
        if (Languages != null)
            parameters.AddRange(Languages.Select(lang => $"language={lang}"));
        if (First != 20)
            parameters.Add($"first={First}");
        if (Before is { Length: > 0 })
            parameters.Add($"before={Before}");
        if (After is { Length: > 0 })
            parameters.Add($"after={After}");

        return string.Join("&", parameters);
    }
}

/// <summary>
/// Twitch streams API response payload.
/// </summary>
/// <param name="Data">Streams returned by the query.</param>
/// <param name="Pagination">Pagination metadata for continuing the query.</param>
public sealed record TwitchStreamsResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<TwitchBroadcast> Data,
    [property: JsonPropertyName("pagination")] Pagination? Pagination);

/// <summary>
/// Twitch live stream with metadata and viewer information.
/// </summary>
public sealed record TwitchBroadcast
{
    /// <summary>
    /// Stream ID for VOD lookup.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Broadcaster user ID.
    /// </summary>
    [JsonPropertyName("user_id")]
    public required string UserId { get; init; }

    /// <summary>
    /// Broadcaster login name.
    /// </summary>
    [JsonPropertyName("user_login")]
    public required string UserLogin { get; init; }

    /// <summary>
    /// Broadcaster display name.
    /// </summary>
    [JsonPropertyName("user_name")]
    public required string UserName { get; init; }

    /// <summary>
    /// Category or game ID.
    /// </summary>
    [JsonPropertyName("game_id")]
    public required string GameId { get; init; }

    /// <summary>
    /// Category or game name.
    /// </summary>
    [JsonPropertyName("game_name")]
    public required string GameName { get; init; }

    /// <summary>
    /// Stream type: "live" or empty.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// Current stream title.
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// Custom stream tags applied to the broadcast.
    /// </summary>
    [JsonPropertyName("tags")]
    public required string[] Tags { get; init; }

    /// <summary>
    /// Current concurrent viewer count.
    /// </summary>
    [JsonPropertyName("viewer_count")]
    public required int ViewerCount { get; init; }

    /// <summary>
    /// When the broadcast began (RFC3339 UTC).
    /// </summary>
    [JsonPropertyName("started_at")]
    public required DateTime StartedAt { get; init; }

    /// <summary>
    /// Stream language (ISO 639-1 or "other").
    /// </summary>
    [JsonPropertyName("language")]
    public required string Language { get; init; }

    /// <summary>
    /// Thumbnail URL with {width}x{height} placeholders.
    /// </summary>
    [JsonPropertyName("thumbnail_url")]
    public required string ThumbnailUrl { get; init; }

    /// <summary>
    /// Indicates whether the stream is marked as mature content.
    /// </summary>
    [JsonPropertyName("is_mature")]
    public required bool IsMature { get; init; }
}