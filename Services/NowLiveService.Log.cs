using Microsoft.Extensions.Logging;

namespace TvGuide.Services;

internal static partial class NowLiveServiceLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Starting")]
    public static partial void ServiceStarting(ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Exiting")]
    public static partial void ServiceExiting(ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Exited")]
    public static partial void ServiceExited(ILogger logger);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Started")]
    public static partial void Started(ILogger logger);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Debug, Message = "User data was saved")]
    public static partial void UserDataSaved(ILogger logger);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning, Message = "User was not found ({UserLogin})")]
    public static partial void UserNotFound(ILogger logger, string userLogin);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Debug, Message = "User data was saved ({UserLogin})")]
    public static partial void UserSavedForLogin(ILogger logger, string userLogin);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Warning, Message = "User exists ({UserId})")]
    public static partial void UserAlreadyExists(ILogger logger, string userId);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Error, Message = "User was not removed ({UserId})")]
    public static partial void UserNotRemoved(ILogger logger, string userId);
}
