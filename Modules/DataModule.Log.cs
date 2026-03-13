using Microsoft.Extensions.Logging;

namespace TvGuide.Modules;

internal static partial class DataModuleLog
{
    [LoggerMessage(EventId = 1250, Level = LogLevel.Warning, Message = "Tracked user data could not be loaded from {FilePath}; starting with empty state")]
    public static partial void FailedToLoadUsers(ILogger logger, Exception exception, string filePath);
}