using Microsoft.Extensions.Logging;

namespace TvGuide.Events;

internal static partial class BroadcastStatesLog
{
    [LoggerMessage(EventId = 1500, Level = LogLevel.Debug, Message = "Stream is now online (Total: {Count})")]
    public static partial void OnlineStateSummary(ILogger logger, int count);

    [LoggerMessage(EventId = 1501, Level = LogLevel.Error, Message = "Failed to process online state for {Count} users")]
    public static partial void FailedOnlineStateProcessing(ILogger logger, Exception exception, int count);

    [LoggerMessage(EventId = 1502, Level = LogLevel.Debug, Message = "Stream is now offline (Total: {Count})")]
    public static partial void OfflineStateSummary(ILogger logger, int count);

    [LoggerMessage(EventId = 1503, Level = LogLevel.Error, Message = "Failed to process offline state for {Count} users")]
    public static partial void FailedOfflineStateProcessing(ILogger logger, Exception exception, int count);

    [LoggerMessage(EventId = 1504, Level = LogLevel.Debug, Message = "Media refresh was processed (Total: {Count})")]
    public static partial void MediaRefreshSummary(ILogger logger, int count);

    [LoggerMessage(EventId = 1505, Level = LogLevel.Error, Message = "Failed to process media refresh for {Count} users")]
    public static partial void FailedMediaRefreshProcessing(ILogger logger, Exception exception, int count);

    [LoggerMessage(EventId = 1506, Level = LogLevel.Error, Message = "Failed to process service starting")]
    public static partial void FailedServiceStarting(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1507, Level = LogLevel.Debug, Message = "Stream has not changed from online (Total: {Count})")]
    public static partial void ContinuingStateSummary(ILogger logger, int count);

    [LoggerMessage(EventId = 1508, Level = LogLevel.Error, Message = "Failed to process unchanged state for {Count} users")]
    public static partial void FailedContinuingStateProcessing(ILogger logger, Exception exception, int count);

    [LoggerMessage(EventId = 1509, Level = LogLevel.Debug, Message = "Now Live service has started successfully")]
    public static partial void ServiceStarted(ILogger logger);

    [LoggerMessage(EventId = 1510, Level = LogLevel.Debug, Message = "Now Live service has exited")]
    public static partial void ServiceExited(ILogger logger);

    [LoggerMessage(EventId = 1511, Level = LogLevel.Debug, Message = "{Count} user(s) added to tracking list")]
    public static partial void UsersAdded(ILogger logger, int count);

    [LoggerMessage(EventId = 1512, Level = LogLevel.Debug, Message = "{Count} user(s) removed from tracking list")]
    public static partial void UsersRemoved(ILogger logger, int count);

    [LoggerMessage(EventId = 1513, Level = LogLevel.Error, Message = "Error processing user stream - UserId: {UserId}, Message: {Message}")]
    public static partial void UserStreamError(ILogger logger, Exception exception, string userId, string message);
}
