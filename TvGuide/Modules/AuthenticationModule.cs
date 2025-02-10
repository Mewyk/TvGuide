using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TvGuide.Modules;

public class AuthenticationModule(
    HttpClient httpClient,
    IOptions<Configuration> settings,
    ILogger<AuthenticationModule> logger)
    : IAuthenticationModule
{
    private readonly ILogger<AuthenticationModule> _logger = logger;
    private readonly HttpClient _httpClient = httpClient;
    private readonly Settings.TwitchAuthentication _settings = settings.Value.Twitch.Authentication;
    private readonly LogMessages _logMessages = settings.Value.LogMessages;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private string? _currentToken;
    private DateTime _tokenExpiration = DateTime.MinValue;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_currentToken) && DateTime.UtcNow < _tokenExpiration)
            return _currentToken;

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_currentToken) && DateTime.UtcNow < _tokenExpiration)
                return _currentToken;

            var parameters = new Dictionary<string, string>
            {
                ["client_id"] = _settings.ClientId,
                ["client_secret"] = _settings.ClientSecret,
                ["grant_type"] = "client_credentials"
            };

            using var content = new FormUrlEncodedContent(parameters);
            using var response = await _httpClient.PostAsync(GetBaseUrl("token"), content, cancellationToken);

            response.EnsureSuccessStatusCode();

            var authResponse = await JsonSerializer.DeserializeAsync<AuthenticationResponse>(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException(_logMessages.Errors.JsonWasNotProcessed);

            _currentToken = authResponse.AccessToken;
            _tokenExpiration = DateTime.UtcNow.AddSeconds(authResponse.ExpiresIn - 300);

            _logger.LogInformation("New authentication token expires at: {Expiration}", _tokenExpiration); // TODO: LogMessage
            return _currentToken;
        }
        finally { _semaphore.Release(); }
    }

    private static string GetBaseUrl(string endpoint) => $"https://id.twitch.tv/oauth2/{endpoint}";
}

public record AuthenticationResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }

    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }
}
