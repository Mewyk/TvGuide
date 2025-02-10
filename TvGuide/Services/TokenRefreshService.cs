using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TvGuide.Services;

public class TokenRefreshService(
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
                await _authenticationModule.GetAccessTokenAsync(stoppingToken);
                _retryCount = 0;
                await Task.Delay(_settings.TokenNormalDelay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Do something
            }
            catch (Exception exception)
            {
                _retryCount++;
                _logger.LogError(
                    exception, 
                    "Error refreshing token - Attempt {RetryCount}", 
                    _retryCount);

                var delay = CalculateDelay(_retryCount);
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    private TimeSpan CalculateDelay(int retryCount)
    {
        retryCount = Math.Min(retryCount, _settings.TokenMaxRetries);
        var delayInSeconds = Math.Pow(2, retryCount);

        return TimeSpan.FromSeconds(
            Math.Min(delayInSeconds, _settings.TokenMaxDelay.TotalSeconds));
    }
}
