using Microsoft.Extensions.Logging;

namespace TvGuide.Modules;

internal static partial class AuthenticationModuleLog
{
    [LoggerMessage(EventId = 1300, Level = LogLevel.Information, Message = "Authentication token acquired, expires at: {Expiration}")]
    public static partial void AuthenticationTokenAcquired(ILogger logger, DateTime expiration);
}
