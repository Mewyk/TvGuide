using Microsoft.Extensions.Logging;

namespace TvGuide.Modules;

internal static partial class UsersModuleLog
{
    [LoggerMessage(EventId = 1400, Level = LogLevel.Error, Message = "Failed to get user info: {StatusCode} - {Content}")]
    public static partial void FailedToGetUserInfo(ILogger logger, System.Net.HttpStatusCode statusCode, string content);
}
