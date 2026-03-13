using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TvGuide.Twitch;

namespace TvGuide.Modules;

/// <summary>
/// Retrieves clip data from the Twitch Helix clips endpoint.
/// </summary>
public sealed class ClipsModule(
    HttpClient httpClient,
    IAuthenticationModule authService,
    IOptions<Configuration> settings) 
    : IClipsModule
{
    private readonly IAuthenticationModule _authService = authService;
    private readonly Settings.Twitch _settings = settings.Value.Twitch;
    private readonly HttpClient _httpClient = httpClient;

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<TwitchClip> Clips, string? NextCursor)> GetClipsAsync(
        TwitchClipRequest parameters,
        CancellationToken cancellationToken = default)
    {
        ValidateParameters(parameters);

        var token = await _authService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, GetBaseUrl($"clips?{parameters.ToQueryString()}"));
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("Client-Id", _settings.Authentication.ClientId);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Request failed: {response.StatusCode} - {errorContent}",
                null,
                response.StatusCode);
        }

        var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync<TwitchClipsResponse>(
            responseStream,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result is null
            ? ([], null)
            : (result.Data, result.Pagination?.Cursor);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TwitchClip> GetAllClipsAsync(
        TwitchClipRequest parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var requestParams = new TwitchClipRequest
            {
                BroadcasterId = parameters.BroadcasterId,
                GameId = parameters.GameId,
                Id = parameters.Id,
                StartedAt = parameters.StartedAt,
                EndedAt = parameters.EndedAt,
                First = parameters.First,
                Before = parameters.Before,
                After = cursor,
                IsFeatured = parameters.IsFeatured
            };
            var (clips, nextCursor) = await GetClipsAsync(requestParams, cancellationToken);

            foreach (var clip in clips)
                yield return clip;

            cursor = nextCursor;
        }
        while (cursor != null);
    }

    private static void ValidateParameters(TwitchClipRequest parameters)
    {
        var nonNullParams = new[] { parameters.BroadcasterId, parameters.GameId, parameters.Id }
            .Count(param => param is { Length: > 0 });

        if (nonNullParams == 0)
            throw new ArgumentException("One of BroadcasterId, GameId, or Id must be specified");

        if (nonNullParams > 1)
            throw new ArgumentException("Only one of BroadcasterId, GameId, or Id can be specified");

        if (parameters.First < 1 || parameters.First > 100)
            throw new ArgumentException("First must be between 1 and 100");
    }

    private static string GetBaseUrl(string endpoint) => $"https://api.twitch.tv/helix/{endpoint}";
}

/// <summary>
/// Twitch clips API request parameters.
/// </summary>
public sealed record TwitchClipRequest
{
    /// <summary>
    /// Broadcaster ID to query clips for.
    /// </summary>
    public string? BroadcasterId { get; init; }

    /// <summary>
    /// Game or category ID to query clips for.
    /// </summary>
    public string? GameId { get; init; }

    /// <summary>
    /// Specific clip ID to retrieve.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Inclusive UTC start time for the clip search window.
    /// </summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>
    /// Inclusive UTC end time for the clip search window.
    /// </summary>
    public DateTime? EndedAt { get; init; }

    /// <summary>
    /// Maximum number of clips to return per page. Valid range: 1 to 100.
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
    /// Whether only featured clips should be returned.
    /// </summary>
    public bool? IsFeatured { get; init; }

    /// <summary>
    /// Converts the request parameters into a Twitch Helix query string.
    /// </summary>
    /// <returns>A URL query string without a leading question mark.</returns>
    public string ToQueryString()
    {
        var parameters = new List<string>();

        if (BroadcasterId is { Length: > 0 })
            parameters.Add($"broadcaster_id={BroadcasterId}");
        if (GameId is { Length: > 0 })
            parameters.Add($"game_id={GameId}");
        if (Id is { Length: > 0 })
            parameters.Add($"id={Id}");
        if (StartedAt.HasValue)
            parameters.Add($"started_at={StartedAt.Value:O}");
        if (EndedAt.HasValue)
            parameters.Add($"ended_at={EndedAt.Value:O}");
        if (First != 20)
            parameters.Add($"first={First}");
        if (Before is { Length: > 0 })
            parameters.Add($"before={Before}");
        if (After is { Length: > 0 })
            parameters.Add($"after={After}");
        if (IsFeatured.HasValue)
            parameters.Add($"is_featured={IsFeatured.Value.ToString().ToLower()}");

        return string.Join("&", parameters);
    }
}

/// <summary>
/// Twitch clip with metadata.
/// </summary>
public sealed record TwitchClip
{
    /// <summary>
    /// Clip identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Public clip URL.
    /// </summary>
    [JsonPropertyName("url")]
    public required string Url { get; init; }

    /// <summary>
    /// Embeddable clip URL.
    /// </summary>
    [JsonPropertyName("embed_url")]
    public required string EmbedUrl { get; init; }

    /// <summary>
    /// Broadcaster user ID for the clipped channel.
    /// </summary>
    [JsonPropertyName("broadcaster_id")]
    public required string BroadcasterId { get; init; }

    /// <summary>
    /// Broadcaster display name for the clipped channel.
    /// </summary>
    [JsonPropertyName("broadcaster_name")]
    public required string BroadcasterName { get; init; }

    /// <summary>
    /// User ID of the clip creator.
    /// </summary>
    [JsonPropertyName("creator_id")]
    public required string CreatorId { get; init; }

    /// <summary>
    /// Display name of the clip creator.
    /// </summary>
    [JsonPropertyName("creator_name")]
    public required string CreatorName { get; init; }

    /// <summary>
    /// Associated VOD identifier when available.
    /// </summary>
    [JsonPropertyName("video_id")]
    public required string VideoId { get; init; }

    /// <summary>
    /// Category or game ID for the clip.
    /// </summary>
    [JsonPropertyName("game_id")]
    public required string GameId { get; init; }

    /// <summary>
    /// Language code associated with the clip.
    /// </summary>
    [JsonPropertyName("language")]
    public required string Language { get; init; }

    /// <summary>
    /// Clip title.
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// Number of views for the clip.
    /// </summary>
    [JsonPropertyName("view_count")]
    public required int ViewCount { get; init; }

    /// <summary>
    /// UTC timestamp when the clip was created.
    /// </summary>
    [JsonPropertyName("created_at")]
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// Thumbnail image URL for the clip.
    /// </summary>
    [JsonPropertyName("thumbnail_url")]
    public required string ThumbnailUrl { get; init; }

    /// <summary>
    /// Clip duration in seconds.
    /// </summary>
    [JsonPropertyName("duration")]
    public required float Duration { get; init; }

    /// <summary>
    /// Offset, in seconds, into the source VOD where the clip begins.
    /// </summary>
    [JsonPropertyName("vod_offset")]
    public int? VodOffset { get; init; }

    /// <summary>
    /// Indicates whether the clip is featured.
    /// </summary>
    [JsonPropertyName("is_featured")]
    public required bool IsFeatured { get; init; }
}

/// <summary>
/// Twitch clips API response payload.
/// </summary>
/// <param name="Data">Clips returned by the query.</param>
/// <param name="Pagination">Pagination metadata for continuing the query.</param>
public sealed record TwitchClipsResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<TwitchClip> Data,
    [property: JsonPropertyName("pagination")] Pagination? Pagination);