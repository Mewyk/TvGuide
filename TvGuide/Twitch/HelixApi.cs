using System.Text.Json.Serialization;

namespace TvGuide.Twitch;

public class Pagination
{
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}
