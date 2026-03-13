using Microsoft.Extensions.Logging;

namespace TvGuide.Modules;

internal static partial class ActiveBroadcastsModuleLog
{
    [LoggerMessage(EventId = 1200, Level = LogLevel.Information, Message = "File was not found ({FilePath})")]
    public static partial void ActiveBroadcastFileNotFound(ILogger logger, string filePath);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "Loaded the data - {Count} broadcast(s) loaded")]
    public static partial void BroadcastsLoaded(ILogger logger, int count);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Error, Message = "Error loading data ({FilePath})")]
    public static partial void FailedToLoadBroadcastData(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Warning, Message = "Exceeded 10 broadcast limit, removing oldest")]
    public static partial void ExceededBroadcastLimit(ILogger logger);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Warning, Message = "Broadcast for {DisplayName} ({UserId}) not found, skipping removal")]
    public static partial void BroadcastNotFoundForRemoval(ILogger logger, string displayName, string userId);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Warning, Message = "Old broadcast message not found, cannot update to offline")]
    public static partial void OldBroadcastMessageNotFound(ILogger logger);

    [LoggerMessage(EventId = 1206, Level = LogLevel.Warning, Message = "Broadcast not found for user {UserId}, cannot update")]
    public static partial void BroadcastNotFoundForUpdate(ILogger logger, string userId);

    [LoggerMessage(EventId = 1207, Level = LogLevel.Warning, Message = "Component count {Count} exceeds 40 limit, truncating")]
    public static partial void ComponentCountExceeded(ILogger logger, int count);

    [LoggerMessage(EventId = 1208, Level = LogLevel.Warning, Message = "Broadcast message not found, recreating")]
    public static partial void BroadcastMessageNotFound(ILogger logger);
}
