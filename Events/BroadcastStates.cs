using Microsoft.Extensions.Logging;
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
    ActiveBroadcastsModule activeBroadcastsModule)
{
    private readonly ILogger<BroadcastStates> _logger = logger;
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
    /// <param name="sender">The event source.</param>
    /// <param name="eventData">Users detected as currently live.</param>
    public async void OnBroadcastDetectedLive(object? sender, UsersEventArguments eventData)
    {
        BroadcastStatesLog.OnlineStateSummary(_logger, eventData.Users.Count);

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
            BroadcastStatesLog.FailedOnlineStateProcessing(_logger, exception, eventData.Users.Count);
        }
    }

    /// <summary>
    /// Handles broadcasts that have ended.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="eventData">Users whose broadcasts have ended.</param>
    public async void OnBroadcastEnded(object? sender, UsersEventArguments eventData)
    {
        BroadcastStatesLog.OfflineStateSummary(_logger, eventData.Users.Count);

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
            BroadcastStatesLog.FailedOfflineStateProcessing(_logger, exception, eventData.Users.Count);
        }
    }

    /// <summary>
    /// Handles broadcasts that need media (preview image) refresh.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="eventData">Users whose active broadcasts need refreshed preview media.</param>
    public async void OnBroadcastMediaRefreshDue(object? sender, UsersEventArguments eventData)
    {
        BroadcastStatesLog.MediaRefreshSummary(_logger, eventData.Users.Count);

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
            BroadcastStatesLog.FailedMediaRefreshProcessing(_logger, exception, eventData.Users.Count);
        }
    }

    /// <summary>
    /// Handles the service-starting event by loading persisted state and rebuilding the status message.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="eventData">Lifecycle data for the service start operation.</param>
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
            BroadcastStatesLog.FailedServiceStarting(_logger, exception);
        }
    }

    /// <summary>
    /// Handles the service-exiting event.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="eventData">Lifecycle data for the service shutdown operation.</param>
    public void OnServiceExiting(object? sender, ServiceEventArguments eventData)
    {
        // Data is already saved after each operation - no action needed
    }

    /// <summary>
    /// Handles broadcasts that are continuing with no state change.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="eventData">Users whose broadcasts are still live without changes.</param>
    public async void OnBroadcastContinuing(object? sender, UsersEventArguments eventData)
    {
        BroadcastStatesLog.ContinuingStateSummary(_logger, eventData.Users.Count);

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
            BroadcastStatesLog.FailedContinuingStateProcessing(_logger, exception, eventData.Users.Count);
        }
    }

    /// <summary>
    /// Handles the service-started event.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="eventData">Generic event data for the service start notification.</param>
    public void OnServiceStarted(object? sender, EventArgs eventData)
        => BroadcastStatesLog.ServiceStarted(_logger);

    /// <summary>
    /// Handles the service-exited event.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="eventData">Generic event data for the service exit notification.</param>
    public void OnServiceExited(object? sender, EventArgs eventData)
        => BroadcastStatesLog.ServiceExited(_logger);

    /// <summary>
    /// Handles the user-added event.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="eventData">Users that were added to tracking.</param>
    public void OnUserAdded(object? sender, UsersEventArguments eventData)
        => BroadcastStatesLog.UsersAdded(_logger, eventData.Users.Count);

    /// <summary>
    /// Handles the user-removed event.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="eventData">Users that were removed from tracking.</param>
    public void OnUserRemoved(object? sender, UsersEventArguments eventData)
        => BroadcastStatesLog.UsersRemoved(_logger, eventData.Users.Count);

    /// <summary>
    /// Handles per-user broadcast processing errors.
    /// </summary>
    /// <param name="sender">The event source.</param>
    /// <param name="eventData">Error details for the failed user update.</param>
    public void OnUserStreamError(object? sender, ErrorEventArguments eventData)
        => BroadcastStatesLog.UserStreamError(_logger, eventData.Exception, eventData.UserId, eventData.Message);
}

/// <summary>
/// Service lifecycle event data.
/// </summary>
public sealed class ServiceEventArguments(CancellationToken cancellationToken) : EventArgs
{
    /// <summary>
    /// Cancellation token associated with the lifecycle event.
    /// </summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;
}

/// <summary>
/// Event data for Twitch streamers.
/// </summary>
public sealed class UsersEventArguments(CancellationToken cancellationToken) : EventArgs
{
    /// <summary>
    /// Users associated with the current event.
    /// </summary>
    public List<TwitchStreamer> Users { get; set; } = [];

    /// <summary>
    /// Cancellation token associated with the current processing operation.
    /// </summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;
}

/// <summary>
/// Error event data for stream processing.
/// </summary>
public sealed class ErrorEventArguments(string userId, string message, Exception exception) : EventArgs
{
    /// <summary>
    /// Twitch user ID that failed during processing.
    /// </summary>
    public string UserId { get; } = userId;

    /// <summary>
    /// Error message associated with the failure.
    /// </summary>
    public string Message { get; } = message;

    /// <summary>
    /// Exception thrown while processing the user.
    /// </summary>
    public Exception Exception { get; } = exception;
}