using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TvGuide.Modules;

public sealed class AuthenticationModule(
    HttpClient httpClient,
    IOptions<Configuration> settings,
    ILogger<AuthenticationModule> logger)
    : IAuthenticationModule, IDisposable
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

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
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
            using var response = await _httpClient.PostAsync(GetBaseUrl("token"), content, cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var authResponse = await JsonSerializer.DeserializeAsync<AuthenticationResponse>(
                    responseStream,
                    cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Failed to deserialize authentication response");

            _currentToken = authResponse.AccessToken;
            _tokenExpiration = DateTime.UtcNow.AddSeconds(authResponse.ExpiresIn - 300);

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Authentication token acquired, expires at: {Expiration}", _tokenExpiration);
            
            return _currentToken;
        }
        finally { _semaphore.Release(); }
    }

    public void Dispose() => _semaphore.Dispose();

    private static string GetBaseUrl(string endpoint) => $"https://id.twitch.tv/oauth2/{endpoint}";
}

/// <summary>
/// Twitch OAuth token response: access token, expiration, and type.
/// </summary>
public sealed record AuthenticationResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("token_type")] string TokenType);
