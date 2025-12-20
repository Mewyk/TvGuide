using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TvGuide.Services;

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
                    _logger.LogInformation("Token refresh service stopping");

                break;
            }
            catch (Exception exception)
            {
                _retryCount++;
                if (_logger.IsEnabled(LogLevel.Error))
                    _logger.LogError(
                        exception, 
                        "Error refreshing token - Attempt {RetryCount}", 
                        _retryCount);

                var delay = CalculateDelay(_retryCount);
                
                try
                {
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                        _logger.LogInformation("Token refresh service stopping during retry delay");

                    break;
                }
            }
        }
    }

    private TimeSpan CalculateDelay(int retryCount)
    {
        var clampedRetries = Math.Min(retryCount, _settings.TokenMaxRetries);
        var exponentialSeconds = Math.Pow(2, clampedRetries);
        var cappedSeconds = Math.Min(exponentialSeconds, _settings.TokenMaxDelay.TotalSeconds);
        return TimeSpan.FromSeconds(cappedSeconds);
    }
}
