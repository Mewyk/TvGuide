using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using TvGuide.Twitch;

namespace TvGuide.Modules;

public class ClipsModule(
    HttpClient httpClient,
    IAuthenticationModule authService,
    IOptions<Configuration> settings) 
    : IClipsModule
{
    private readonly IAuthenticationModule _authService = authService;
    private readonly Settings.Twitch _settings = settings.Value.Twitch;
    private readonly HttpClient _httpClient = httpClient;

    public async Task<(IReadOnlyList<TwitchClip> Clips, string? NextCursor)> GetClipsAsync(
        TwitchClipRequest parameters,
        CancellationToken cancellationToken = default)
    {
        ValidateParameters(parameters);

        var token = await _authService.GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, GetBaseUrl($"clips?{parameters.ToQueryString()}"));
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("Client-Id", _settings.Authentication.ClientId);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception(
                $"Request failed: {response.StatusCode} - {await response.Content.ReadAsStringAsync(cancellationToken)}");

        var result = await JsonSerializer.DeserializeAsync<TwitchClipsResponse>(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        return result is null
            ? ((IReadOnlyList<TwitchClip> Clips, string? NextCursor))([], null)
            : ((IReadOnlyList<TwitchClip> Clips, string? NextCursor))(result.Data, result.Pagination?.Cursor);
    }

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
            .Count(param => !string.IsNullOrEmpty(param));

        if (nonNullParams == 0)
            throw new ArgumentException("One of BroadcasterId, GameId, or Id must be specified");

        if (nonNullParams > 1)
            throw new ArgumentException("Only one of BroadcasterId, GameId, or Id can be specified");

        if (parameters.First < 1 || parameters.First > 100)
            throw new ArgumentException("First must be between 1 and 100");
    }

    // TODO: Do this properly
    private static string GetBaseUrl(string endpoint) => $"https://api.twitch.tv/helix/{endpoint}";
}

public class TwitchClipRequest
{
    public string? BroadcasterId { get; init; }
    public string? GameId { get; init; }
    public string? Id { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public int First { get; init; } = 20;
    public string? Before { get; init; }
    public string? After { get; init; }
    public bool? IsFeatured { get; init; }

    public string ToQueryString()
    {
        var parameters = new List<string>();

        if (!string.IsNullOrEmpty(BroadcasterId))
            parameters.Add($"broadcaster_id={BroadcasterId}");
        if (!string.IsNullOrEmpty(GameId))
            parameters.Add($"game_id={GameId}");
        if (!string.IsNullOrEmpty(Id))
            parameters.Add($"id={Id}");
        if (StartedAt.HasValue)
            parameters.Add($"started_at={StartedAt.Value:O}");
        if (EndedAt.HasValue)
            parameters.Add($"ended_at={EndedAt.Value:O}");
        if (First != 20)
            parameters.Add($"first={First}");
        if (!string.IsNullOrEmpty(Before))
            parameters.Add($"before={Before}");
        if (!string.IsNullOrEmpty(After))
            parameters.Add($"after={After}");
        if (IsFeatured.HasValue)
            parameters.Add($"is_featured={IsFeatured.Value.ToString().ToLower()}");

        return string.Join("&", parameters);
    }
}

public class TwitchClip
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("embed_url")]
    public required string EmbedUrl { get; init; }

    [JsonPropertyName("broadcaster_id")]
    public required string BroadcasterId { get; init; }

    [JsonPropertyName("broadcaster_name")]
    public required string BroadcasterName { get; init; }

    [JsonPropertyName("creator_id")]
    public required string CreatorId { get; init; }

    [JsonPropertyName("creator_name")]
    public required string CreatorName { get; init; }

    [JsonPropertyName("video_id")]
    public required string VideoId { get; init; }

    [JsonPropertyName("game_id")]
    public required string GameId { get; init; }

    [JsonPropertyName("language")]
    public required string Language { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("view_count")]
    public required int ViewCount { get; init; }

    [JsonPropertyName("created_at")]
    public required DateTime CreatedAt { get; init; }

    [JsonPropertyName("thumbnail_url")]
    public required string ThumbnailUrl { get; init; }

    [JsonPropertyName("duration")]
    public required float Duration { get; init; }

    [JsonPropertyName("vod_offset")]
    public int? VodOffset { get; init; }

    [JsonPropertyName("is_featured")]
    public required bool IsFeatured { get; init; }
}

public class TwitchClipsResponse
{
    [JsonPropertyName("data")]
    public required IReadOnlyList<TwitchClip> Data { get; init; }

    [JsonPropertyName("pagination")]
    public Pagination? Pagination { get; init; }
}