using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TvGuide.Modules;

namespace TvGuide.Events;

/// <summary>
/// Event handlers for broadcast state changes detected by NowLiveService.
/// </summary>
/// <remarks>
/// <para><b>Design Pattern: Idempotent Restart Handling</b></para>
/// 
/// This class implements a critical pattern for handling application restarts without creating duplicate Discord messages:
/// 
/// <para><b>Event Flow on Restart:</b></para>
/// <list type="number">
/// <item>App starts → ServiceStarting event fires → LoadDataAsync restores broadcast data from JSON</item>
/// <item>NowLiveService checks which streams are live</item>
/// <item>For each live stream → BroadcastDetectedLive event fires</item>
/// <item>Handler checks IsMessageTracked:</item>
/// <list type="bullet">
/// <item>Message exists (loaded from JSON) → UpdateBroadcastMessageAsync (update existing Discord message)</item>
/// <item>Message doesn't exist (truly new) → CreateBroadcastMessageAsync (create new Discord message)</item>
/// </list>
/// </list>
/// 
/// <para><b>Key Insight:</b></para>
/// "DetectedLive" does NOT mean "just went live" - it means "IS CURRENTLY live when checked."
/// The IsMessageTracked check distinguishes between new and existing broadcasts.
/// 
/// <para><b>Why This Matters:</b></para>
/// Without the IsMessageTracked check, every restart would create duplicate messages for already-tracked streams.
/// This pattern makes the system idempotent - safe to restart without side effects.
/// </remarks>
public sealed class BroadcastStates(
    ILogger<BroadcastStates> logger,
    IOptions<Configuration> settings,
    ActiveBroadcastsModule activeBroadcastsModule)
{
    private readonly ILogger<BroadcastStates> _logger = logger;
    private readonly LogMessages _logMessages = settings.Value.LogMessages;
    private readonly ActiveBroadcastsModule _activeBroadcasts = activeBroadcastsModule;

    /// <summary>
    /// Handles broadcasts detected as live.
    /// </summary>
    /// <remarks>
    /// CRITICAL: This fires when a stream IS DETECTED as live, which includes:
    /// - Newly started streams (user just went live)
    /// - Already-tracked streams after app restart
    /// 
    /// The IsMessageTracked check ensures idempotent behavior:
    /// - Not tracked → CreateBroadcastMessageAsync (new Discord message)
    /// - Already tracked → UpdateBroadcastMessageAsync (update existing message)
    /// 
    /// This design prevents duplicate messages when the app restarts.
    /// </remarks>
    public async void OnBroadcastDetectedLive(object? sender, UsersEventArguments eventData)
    {
        _logger.LogDebug("{LogMessage} (Total: {Count})", 
            _logMessages.StreamIsOnline, eventData.Users.Count);
        await ProcessDetectedLiveAsync(eventData).ConfigureAwait(false);
    }

    private async Task ProcessDetectedLiveAsync(UsersEventArguments eventData)
    {
        try
        {
            var tasks = eventData.Users.Select(user =>
                !_activeBroadcasts.IsMessageTracked(user)
                    ? _activeBroadcasts.CreateBroadcastMessageAsync(user, eventData.CancellationToken)
                    : _activeBroadcasts.UpdateBroadcastMessageAsync(user, eventData.CancellationToken));

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to process online state for {Count} users", eventData.Users.Count);
        }
    }

    /// <summary>
    /// Handles broadcasts that have ended.
    /// </summary>
    public async void OnBroadcastEnded(object? sender, UsersEventArguments eventData)
    {
        _logger.LogDebug("{LogMessage} (Total: {Count})", 
            _logMessages.StreamIsOffline, eventData.Users.Count);
        await ProcessEndedBroadcastsAsync(eventData).ConfigureAwait(false);
    }

    private async Task ProcessEndedBroadcastsAsync(UsersEventArguments eventData)
    {
        try
        {
            var tasks = eventData.Users.Select(user =>
                _activeBroadcasts.RemoveBroadcastMessageAsync(user, eventData.CancellationToken));

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to process offline state for {Count} users", eventData.Users.Count);
        }
    }

    /// <summary>
    /// Handles broadcasts that need media (preview image) refresh.
    /// </summary>
    public async void OnBroadcastMediaRefreshDue(object? sender, UsersEventArguments eventData)
    {
        _logger.LogDebug("{LogMessage} (Total: {Count})", 
            _logMessages.StreamMediaWasRefreshed, eventData.Users.Count);
        await ProcessMediaRefreshAsync(eventData).ConfigureAwait(false);
    }

    private async Task ProcessMediaRefreshAsync(UsersEventArguments eventData)
    {
        try
        {
            var tasks = eventData.Users.Select(user => 
                !_activeBroadcasts.IsMessageTracked(user)
                ? _activeBroadcasts.CreateBroadcastMessageAsync(user, eventData.CancellationToken)
                : _activeBroadcasts.UpdateBroadcastMessageAsync(user, eventData.CancellationToken));

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to process media refresh for {Count} users", eventData.Users.Count);
        }
    }

    public async void OnServiceStarting(object? sender, ServiceEventArguments eventData)
    {
        await ProcessServiceStartingAsync(eventData).ConfigureAwait(false);
    }

    private async Task ProcessServiceStartingAsync(ServiceEventArguments eventData)
    {
        try
        {
            await _activeBroadcasts.LoadDataAsync(eventData.CancellationToken).ConfigureAwait(false);
            await _activeBroadcasts.EnsureStatusMessageExistsAsync(eventData.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to process service starting");
        }
    }

    public void OnServiceExiting(object? sender, ServiceEventArguments eventData)
    {
        // Data is already saved after each operation - no action needed
    }

    /// <summary>
    /// Handles broadcasts that are continuing with no state change.
    /// </summary>
    public async void OnBroadcastContinuing(object? sender, UsersEventArguments eventData)
    {
        _logger.LogDebug("{LogMessage} (Total: {Count})", 
            _logMessages.StreamIsUnchanged, eventData.Users.Count);
        await ProcessContinuingBroadcastsAsync(eventData).ConfigureAwait(false);
    }

    private async Task ProcessContinuingBroadcastsAsync(UsersEventArguments eventData)
    {
        try
        {
            var tasks = eventData.Users.Select(user =>
                !_activeBroadcasts.IsMessageTracked(user)
                ? _activeBroadcasts.CreateBroadcastMessageAsync(user, eventData.CancellationToken)
                : _activeBroadcasts.UpdateBroadcastMessageAsync(user, eventData.CancellationToken));

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to process unchanged state for {Count} users", eventData.Users.Count);
        }
    }

    public void OnServiceStarted(object? sender, EventArgs eventData)
        => _logger.LogDebug("Now Live service has started successfully");

    public void OnServiceExited(object? sender, EventArgs eventData)
        => _logger.LogDebug("Now Live service has exited");

    public void OnUserAdded(object? sender, UsersEventArguments eventData)
        => _logger.LogDebug("{Count} user(s) added to tracking list", eventData.Users.Count);

    public void OnUserRemoved(object? sender, UsersEventArguments eventData)
        => _logger.LogDebug("{Count} user(s) removed from tracking list", eventData.Users.Count);

    public void OnUserStreamError(object? sender, ErrorEventArguments eventData)
        => _logger.LogError(
            eventData.Exception,
            "Error processing user stream - UserId: {UserId}, Message: {Message}",
            eventData.UserId, eventData.Message);
}

/// <summary>
/// Service lifecycle event data.
/// </summary>
public sealed class ServiceEventArguments(CancellationToken cancellationToken) : EventArgs
{
    public CancellationToken CancellationToken { get; } = cancellationToken;
}

/// <summary>
/// Event data for Twitch streamers.
/// </summary>
public sealed class UsersEventArguments(CancellationToken cancellationToken) : EventArgs
{
    public List<TwitchStreamer> Users { get; set; } = [];
    public CancellationToken CancellationToken { get; } = cancellationToken;
}

/// <summary>
/// Error event data for stream processing.
/// </summary>
public sealed class ErrorEventArguments(string userId, string message, Exception exception) : EventArgs
{
    public string UserId { get; } = userId;
    public string Message { get; } = message;
    public Exception Exception { get; } = exception;
}