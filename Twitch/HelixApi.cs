using System.Text.Json.Serialization;

namespace TvGuide.Twitch;

/// <summary>
/// Twitch pagination cursor.
/// </summary>
public sealed record Pagination([property: JsonPropertyName("cursor")] string? Cursor);
