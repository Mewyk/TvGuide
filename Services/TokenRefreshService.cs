using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TvGuide.Services;

/// <summary>
/// Background service that keeps the cached Twitch access token refreshed.
/// </summary>
/// <remarks>
/// After successful refreshes, the service waits for <see cref="Settings.TwitchToken.TokenNormalDelay"/>.
/// Failures retry using exponential backoff capped by <see cref="Settings.TwitchToken.TokenMaxRetries"/>
/// and <see cref="Settings.TwitchToken.TokenMaxDelay"/>.
/// </remarks>
public sealed class TokenRefreshService(
    IAuthenticationModule authenticationModule,
    ILogger<TokenRefreshService> logger,
    IOptions<Configuration> settings)
    : BackgroundService
{
    private readonly IAuthenticationModule _authenticationModule = authenticationModule;
    private readonly ILogger<TokenRefreshService> _logger = logger;
    private readonly Settings.TwitchToken _settings = settings.Value.Twitch.Token;
    private int _retryCount;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _authenticationModule.GetAccessTokenAsync(stoppingToken).ConfigureAwait(false);
                _retryCount = 0;
                await Task.Delay(_settings.TokenNormalDelay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                TokenRefreshServiceLog.ServiceStopping(_logger);

                break;
            }
            catch (Exception exception)
            {
                _retryCount++;
                TokenRefreshServiceLog.TokenRefreshError(_logger, exception, _retryCount);

                var delay = CalculateDelay(_retryCount);
                
                try
                {
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    TokenRefreshServiceLog.ServiceStoppingDuringRetryDelay(_logger);

                    break;
                }
            }
        }
    }

    /// <summary>
    /// Calculates the retry delay for a failed token refresh attempt.
    /// </summary>
    /// <param name="retryCount">The number of consecutive failed attempts.</param>
    /// <returns>An exponential-backoff delay capped by the configured maximums.</returns>
    private TimeSpan CalculateDelay(int retryCount)
    {
        var clampedRetries = Math.Min(retryCount, _settings.TokenMaxRetries);
        var exponentialSeconds = Math.Pow(2, clampedRetries);
        var cappedSeconds = Math.Min(exponentialSeconds, _settings.TokenMaxDelay.TotalSeconds);
        return TimeSpan.FromSeconds(cappedSeconds);
    }
}
