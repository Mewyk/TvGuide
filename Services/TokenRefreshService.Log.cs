using Microsoft.Extensions.Logging;

namespace TvGuide.Services;

internal static partial class TokenRefreshServiceLog
{
    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Token refresh service stopping")]
    public static partial void ServiceStopping(ILogger logger);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Error, Message = "Error refreshing token - Attempt {RetryCount}")]
    public static partial void TokenRefreshError(ILogger logger, Exception exception, int retryCount);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Information, Message = "Token refresh service stopping during retry delay")]
    public static partial void ServiceStoppingDuringRetryDelay(ILogger logger);
}
