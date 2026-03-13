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
    private int _retryCount = 0;

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
                if (_logger.IsEnabled(LogLevel.Information))
                    Log.ServiceStopping(_logger);

                break;
            }
            catch (Exception exception)
            {
                _retryCount++;
                if (_logger.IsEnabled(LogLevel.Error))
                    Log.TokenRefreshError(_logger, exception, _retryCount);

                var delay = CalculateDelay(_retryCount);
                
                try
                {
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                        Log.ServiceStoppingDuringRetryDelay(_logger);

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

    private static partial class Log
    {
        [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Token refresh service stopping")]
        public static partial void ServiceStopping(ILogger logger);

        [LoggerMessage(EventId = 1101, Level = LogLevel.Error, Message = "Error refreshing token - Attempt {RetryCount}")]
        public static partial void TokenRefreshError(ILogger logger, Exception exception, int retryCount);

        [LoggerMessage(EventId = 1102, Level = LogLevel.Information, Message = "Token refresh service stopping during retry delay")]
        public static partial void ServiceStoppingDuringRetryDelay(ILogger logger);
    }
}
