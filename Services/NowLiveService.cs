using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TvGuide.Events;
using TvGuide.Models;
using TvGuide.Modules;
using TwitchSharp;
using TwitchSharp.Api;

namespace TvGuide.Services;

/// <summary>
/// Background service that monitors Twitch streams and emits broadcast state events.
/// </summary>
/// <remarks>
/// <para><b>Event Semantics:</b></para>
/// 
/// <para><b>BroadcastDetectedLive:</b> Stream IS CURRENTLY live when checked</para>
/// <list type="bullet">
/// <item>Fires for newly started streams (user just went live)</item>
/// <item>Fires for already-tracked streams after app restart</item>
/// <item>Handlers MUST check IsMessageTracked to avoid duplicates</item>
/// </list>
/// 
/// <para><b>BroadcastEnded:</b> Stream was live, now offline</para>
/// <list type="bullet">
/// <item>User was tracked as live (IsLive = true)</item>
/// <item>Latest check shows stream is offline</item>
/// </list>
/// 
/// <para><b>BroadcastContinuing:</b> Stream is still live, no state change</para>
/// <list type="bullet">
/// <item>Stream was live and still is live</item>
/// <item>No media refresh needed yet</item>
/// </list>
/// 
/// <para><b>BroadcastMediaRefreshDue:</b> Stream is live and preview image needs refresh</para>
/// <list type="bullet">
/// <item>Stream continuing but NextMediaRefresh time reached</item>
/// <item>Triggers download of fresh preview image</item>
/// </list>
/// 
/// <para><b>Restart Behavior:</b></para>
/// On startup, ServiceStarting fires → broadcasts are loaded → first check detects all currently live streams
/// → BroadcastDetectedLive fires for each → handlers use IsMessageTracked to decide create vs update.
/// </remarks>
public sealed class NowLiveService(
    DataModule data,
    TwitchApiClient twitchApiClient,
    IOptions<Configuration> settings,
    ILogger<NowLiveService> logger,
    BroadcastStates broadcastState)
    : BackgroundService, INowLiveService
{
    private readonly TwitchApiClient _twitchApiClient = twitchApiClient;
    private readonly ILogger<NowLiveService> _logger = logger;
    private readonly DataModule _data = data;
    private readonly Settings.NowLive _settings = settings.Value.NowLive;
    private readonly ConcurrentDictionary<string, TrackedTwitchUser> _users = new(StringComparer.Ordinal);
    private readonly BroadcastStates _broadcastState = broadcastState;

    // Bulk event handlers
    /// <summary>
    /// Raised when tracked users are detected as live during a polling cycle.
    /// </summary>
    public event EventHandler<UsersEventArgs>? BroadcastDetectedLive;

    /// <summary>
    /// Raised when tracked users transition from live to offline.
    /// </summary>
    public event EventHandler<UsersEventArgs>? BroadcastEnded;

    /// <summary>
    /// Raised when tracked users remain live with no state change.
    /// </summary>
    public event EventHandler<UsersEventArgs>? BroadcastContinuing;

    /// <summary>
    /// Raised when active broadcasts require preview-media refresh.
    /// </summary>
    public event EventHandler<UsersEventArgs>? BroadcastMediaRefreshDue;

    // Service lifecycle event handlers
    /// <summary>
    /// Raised before persisted state is loaded during service startup.
    /// </summary>
    public event EventHandler<ServiceEventArgs>? ServiceStarting;

    /// <summary>
    /// Raised after the service has started.
    /// </summary>
    public event EventHandler<ServiceEventArgs>? ServiceStarted;

    /// <summary>
    /// Raised when the service is shutting down and cleanup is beginning.
    /// </summary>
    public event EventHandler<ServiceEventArgs>? ServiceExiting;

    /// <summary>
    /// Raised after the service has completed shutdown.
    /// </summary>
    public event EventHandler<ServiceEventArgs>? ServiceExited;

    // Single user event handlers
    /// <summary>
    /// Raised when a user is added to the tracking list.
    /// </summary>
    public event EventHandler<UsersEventArgs>? UserAdded;

    /// <summary>
    /// Raised when a user is removed from the tracking list.
    /// </summary>
    public event EventHandler<UsersEventArgs>? UserRemoved;

    /// <summary>
    /// Raised when processing a user's broadcast state results in an exception.
    /// </summary>
    public event EventHandler<Events.ErrorEventArgs>? UserBroadcastError;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer periodicTimer = 
            new(TimeSpan.FromSeconds(_settings.UpdateInterval));

        try
        {
            NowLiveServiceLog.ServiceStarting(_logger);

            RegisterEventHandlers();

            ServiceStarting?.Invoke(this, new ServiceEventArgs(stoppingToken));

            await LoadUsersAsync(stoppingToken).ConfigureAwait(false);
            await UpdateBroadcastStatesAsync(stoppingToken).ConfigureAwait(false);

            while (await periodicTimer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                await UpdateBroadcastStatesAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            NowLiveServiceLog.ServiceExiting(_logger);

            ServiceExiting?.Invoke(this, new ServiceEventArgs(stoppingToken));

            await _data.SaveUsersAsync(_users.Values, CancellationToken.None).ConfigureAwait(false);
            UnregisterEventHandlers();
        }
        finally
        {
            NowLiveServiceLog.ServiceExited(_logger);

            ServiceExited?.Invoke(this, new ServiceEventArgs(CancellationToken.None));
        }
    }

    private void RegisterEventHandlers()
    {
        BroadcastDetectedLive += _broadcastState.OnBroadcastDetectedLive;
        BroadcastEnded += _broadcastState.OnBroadcastEnded;
        BroadcastContinuing += _broadcastState.OnBroadcastContinuing;
        BroadcastMediaRefreshDue += _broadcastState.OnBroadcastMediaRefreshDue;

        UserAdded += _broadcastState.OnUserAdded;
        UserRemoved += _broadcastState.OnUserRemoved;
        UserBroadcastError += _broadcastState.OnUserStreamError;

        ServiceStarting += _broadcastState.OnServiceStarting;
        ServiceStarted += _broadcastState.OnServiceStarted;
        ServiceExiting += _broadcastState.OnServiceExiting;
        ServiceExited += _broadcastState.OnServiceExited;
    }

    private void UnregisterEventHandlers()
    {
        BroadcastDetectedLive -= _broadcastState.OnBroadcastDetectedLive;
        BroadcastEnded -= _broadcastState.OnBroadcastEnded;
        BroadcastContinuing -= _broadcastState.OnBroadcastContinuing;
        BroadcastMediaRefreshDue -= _broadcastState.OnBroadcastMediaRefreshDue;

        UserAdded -= _broadcastState.OnUserAdded;
        UserRemoved -= _broadcastState.OnUserRemoved;
        UserBroadcastError -= _broadcastState.OnUserStreamError;

        ServiceStarting -= _broadcastState.OnServiceStarting;
        ServiceStarted -= _broadcastState.OnServiceStarted;
        ServiceExiting -= _broadcastState.OnServiceExiting;
        ServiceExited -= _broadcastState.OnServiceExited;
    }

    /// <inheritdoc/>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        NowLiveServiceLog.Started(_logger);

        ServiceStarted?.Invoke(this, new ServiceEventArgs(cancellationToken));

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateBroadcastStatesAsync(CancellationToken cancellationToken)
    {
        var userBatches = _users.Keys.Chunk(_settings.MaxUsersPerRequest);

        foreach (var batch in userBatches)
        {
            var detectedLive = new List<TrackedTwitchUser>();
            var endedBroadcasts = new List<TrackedTwitchUser>();
            var continuingBroadcasts = new List<TrackedTwitchUser>();
            var refreshDue = new List<TrackedTwitchUser>();

            try
            {
                var liveStreamsPage = await ExecuteTwitchOperationAsync(
                    ct => _twitchApiClient.Streams.GetStreamsAsync(
                        userIds: batch,
                        type: "live",
                        first: batch.Length,
                        cancellationToken: ct),
                    $"polling live streams for {batch.Length} tracked user(s)",
                    cancellationToken).ConfigureAwait(false);

                if (liveStreamsPage is null)
                {
                    foreach (var userId in batch)
                    {
                        var args = new Events.ErrorEventArgs(userId, "Broadcast state was not updated", new InvalidOperationException("Twitch stream polling failed."));
                        UserBroadcastError?.Invoke(this, args);
                    }

                    continue;
                }

                var liveStreamsByUserId = liveStreamsPage.Data
                    .ToDictionary(stream => stream.UserId, StringComparer.Ordinal);

                foreach (var userId in batch)
                {
                    if (!_users.TryGetValue(userId, out var user))
                    {
                        continue;
                    }

                    user.RefreshStreamData(liveStreamsByUserId.GetValueOrDefault(userId));

                    UpdateUserState(
                        user,
                        detectedLive,
                        endedBroadcasts,
                        continuingBroadcasts,
                        refreshDue);
                }

                if (detectedLive.Count != 0)
                {
                    var args = new UsersEventArgs(cancellationToken) { Users = detectedLive };
                    BroadcastDetectedLive?.Invoke(this, args);
                }

                if (endedBroadcasts.Count != 0)
                {
                    var args = new UsersEventArgs(cancellationToken) { Users = endedBroadcasts };
                    BroadcastEnded?.Invoke(this, args);
                }

                if (continuingBroadcasts.Count != 0)
                {
                    var args = new UsersEventArgs(cancellationToken) { Users = continuingBroadcasts };
                    BroadcastContinuing?.Invoke(this, args);
                }

                if (refreshDue.Count != 0)
                {
                    var args = new UsersEventArgs(cancellationToken) { Users = refreshDue };
                    BroadcastMediaRefreshDue?.Invoke(this, args);
                }
            }
            catch (Exception exception)
            {
                NowLiveServiceLog.BroadcastBatchFailed(_logger, exception, batch.Length);

                foreach (var userId in batch)
                {
                    var args = new Events.ErrorEventArgs(userId, "Broadcast state was not updated", exception);
                    UserBroadcastError?.Invoke(this, args);
                }
            }
        }
        
        await _data.SaveUsersAsync(_users.Values, cancellationToken).ConfigureAwait(false);

        NowLiveServiceLog.UserDataSaved(_logger);
    }

    private void UpdateUserState(
        TrackedTwitchUser user,
        List<TrackedTwitchUser> detectedLive,
        List<TrackedTwitchUser> endedBroadcasts,
        List<TrackedTwitchUser> continuingBroadcasts,
        List<TrackedTwitchUser> refreshDue)
    {
        bool isOnline = user.StreamData != null;
        var now = DateTimeOffset.UtcNow;

        if (isOnline)
        {
            if (!user.IsLive)
            {
                user.IsLive = true;
                user.LastOnline = now;
                user.NextMediaRefresh = now.AddMinutes(_settings.MediaRefreshInterval);
                detectedLive.Add(user);
            }
            else
            {
                if (user.NextMediaRefresh.HasValue && now >= user.NextMediaRefresh.Value)
                {
                    refreshDue.Add(user);
                    user.NextMediaRefresh = now.AddMinutes(_settings.MediaRefreshInterval);
                }
                else continuingBroadcasts.Add(user);
            }
        }
        else if (user.IsLive)
        {
            user.IsLive = false;
            user.LastOnline = null;
            user.NextMediaRefresh = null;
            endedBroadcasts.Add(user);
        }
    }

    /// <inheritdoc/>
    public async Task<UserManagementResult> AddUserAsync(
        string userLogin, 
        CancellationToken cancellationToken)
    {
        try
        {
            var normalizedUserLogin = userLogin.Trim();

            if (normalizedUserLogin.Length == 0)
            {
                NowLiveServiceLog.UserNotFound(_logger, userLogin);

                return UserManagementResult.NotFound;
            }

            var users = await ExecuteTwitchOperationAsync(
                ct => _twitchApiClient.Users.GetUsersAsync(logins: [normalizedUserLogin], cancellationToken: ct),
                $"looking up Twitch user '{normalizedUserLogin}'",
                cancellationToken)
                .ConfigureAwait(false);

            if (users is null)
                return UserManagementResult.Error;

            var user = users.Count == 0 ? null : users[0];

            if (user == null)
            {
                NowLiveServiceLog.UserNotFound(_logger, normalizedUserLogin);

                return UserManagementResult.NotFound;
            }

            var twitchStreamer = TrackedTwitchUser.Create(user);

            if (_users.TryAdd(twitchStreamer.UserData.Id, twitchStreamer))
            {
                var args = new UsersEventArgs(cancellationToken) { Users = [twitchStreamer] };
                UserAdded?.Invoke(this, args);

                await _data.SaveUsersAsync(_users.Values, cancellationToken).ConfigureAwait(false);

                NowLiveServiceLog.UserSavedForLogin(_logger, normalizedUserLogin);

                return UserManagementResult.Success;
            }

            NowLiveServiceLog.UserAlreadyExists(_logger, twitchStreamer.UserData.Id);

            return UserManagementResult.AlreadyExists;
        }
        catch (Exception exception)
        {
            NowLiveServiceLog.UserLookupFailed(_logger, exception, userLogin);

            return UserManagementResult.Error;
        }
    }

    /// <inheritdoc/>
    public async Task<UserManagementResult> RemoveUserAsync(
        string userLogin, 
        CancellationToken cancellationToken)
    {
        var userId = _users.Values
            .FirstOrDefault(user => 
                user.UserData.Login.Equals(userLogin, StringComparison.OrdinalIgnoreCase))
            ?.UserData.Id;

        if (userId == null)
        {
            NowLiveServiceLog.UserNotFound(_logger, userLogin);

            return UserManagementResult.NotFound;
        }

        if (_users.TryRemove(userId, out var user))
        {
            var args = new UsersEventArgs(cancellationToken) { Users = [user] };
            UserRemoved?.Invoke(this, args);

            await _data.SaveUsersAsync(_users.Values, cancellationToken).ConfigureAwait(false);
            return UserManagementResult.Success;
        }

        NowLiveServiceLog.UserNotRemoved(_logger, userId);

        return UserManagementResult.Error;
    }

    private async Task LoadUsersAsync(CancellationToken cancellationToken)
    {
        var persistedUsers = await _data.LoadUsersAsync(cancellationToken).ConfigureAwait(false);

        var validUsers = persistedUsers
            .Where(u => !string.IsNullOrEmpty(u.UserData.Id))
            .ToList();

        foreach (var user in validUsers)
            _users.TryAdd(user.UserData.Id, user);

        foreach (var batch in validUsers.Chunk(_settings.MaxUsersPerRequest))
        {
            var userIds = batch.Select(user => user.UserData.Id).ToArray();
            var refreshedUsers = await ExecuteTwitchOperationAsync(
                ct => _twitchApiClient.Users.GetUsersAsync(ids: userIds, cancellationToken: ct),
                $"rehydrating {batch.Length} tracked user(s)",
                cancellationToken).ConfigureAwait(false);

            if (refreshedUsers is null)
                continue;

            var refreshedUsersById = refreshedUsers
                .ToDictionary(user => user.Id, StringComparer.Ordinal);

            foreach (var trackedUser in batch)
            {
                if (refreshedUsersById.TryGetValue(trackedUser.UserData.Id, out var refreshedUser))
                    trackedUser.RefreshUserData(refreshedUser);
            }
        }
    }

    private async Task<T?> ExecuteTwitchOperationAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
        where T : class
    {
        const int maxAttempts = 2;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (TwitchApiException exception) when (attempt < maxAttempts && ShouldRetryTwitchOperation(exception))
            {
                LogTwitchApiFailure(operationName, exception);

                var retryDelay = GetRetryDelay(exception);
                NowLiveServiceLog.RetryingTwitchOperation(_logger, operationName, retryDelay);

                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (TwitchApiException exception)
            {
                LogTwitchApiFailure(operationName, exception);
                return null;
            }
        }

        return null;
    }

    private void LogTwitchApiFailure(string operationName, TwitchApiException exception)
    {
        NowLiveServiceLog.TwitchOperationFailed(
            _logger,
            exception,
            operationName,
            exception.Code,
            exception.StatusCode?.ToString(),
            exception.Endpoint,
            exception.IsRateLimited,
            exception.IsUnauthorized,
            exception.IsTransient);
    }

    private static TimeSpan GetRetryDelay(TwitchApiException exception)
    {
        if (exception.TryGetRetryDelay(out var retryDelay))
            return retryDelay;

        return exception.Code switch
        {
            TwitchErrorCodes.RateLimited or
            TwitchErrorCodes.LocalRateLimitQueueFull or
            TwitchErrorCodes.TooManyRequests => TimeSpan.FromSeconds(10),
            TwitchErrorCodes.ServerError => TimeSpan.FromSeconds(5),
            TwitchErrorCodes.NetworkError or
            TwitchErrorCodes.Timeout => TimeSpan.FromSeconds(2),
            _ when exception.IsTransient => TimeSpan.FromSeconds(2),
            _ => TimeSpan.Zero
        };
    }

    private static bool ShouldRetryTwitchOperation(TwitchApiException exception) =>
        exception.Code is
            TwitchErrorCodes.RateLimited or
            TwitchErrorCodes.LocalRateLimitQueueFull or
            TwitchErrorCodes.TooManyRequests or
            TwitchErrorCodes.NetworkError or
            TwitchErrorCodes.Timeout or
            TwitchErrorCodes.ServerError;
}
