using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TvGuide.Events;
using TvGuide.Modules;

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
    IStreamsModule streamsModule,
    IUsersModule usersModule,
    IOptions<Configuration> settings,
    ILogger<NowLiveService> logger,
    BroadcastStates broadcastState
) : BackgroundService, INowLiveService
{
    private readonly IStreamsModule _streamsModule = streamsModule;
    private readonly IUsersModule _usersModule = usersModule;
    private readonly ILogger<NowLiveService> _logger = logger;
    private readonly DataModule _data = data;
    private readonly Settings.NowLive _settings = settings.Value.NowLive;
    private readonly LogMessages _logMessages = settings.Value.LogMessages;
    private readonly ConcurrentDictionary<string, TwitchStreamer> _users = new();
    private readonly BroadcastStates _broadcastState = broadcastState;

    // Bulk event handlers
    public event EventHandler<UsersEventArguments>? BroadcastDetectedLive;
    public event EventHandler<UsersEventArguments>? BroadcastEnded;
    public event EventHandler<UsersEventArguments>? BroadcastContinuing;
    public event EventHandler<UsersEventArguments>? BroadcastMediaRefreshDue;

    // Service lifecycle event handlers
    public event EventHandler<ServiceEventArguments>? ServiceStarting;
    public event EventHandler<ServiceEventArguments>? ServiceStarted;
    public event EventHandler<ServiceEventArguments>? ServiceExiting;
    public event EventHandler<ServiceEventArguments>? ServiceExited;

    // Single user event handlers
    public event EventHandler<UsersEventArguments>? UserAdded;
    public event EventHandler<UsersEventArguments>? UserRemoved;

    public event EventHandler<ErrorEventArguments>? UserBroadcastError;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer periodicTimer = 
            new(TimeSpan.FromSeconds(_settings.UpdateInterval));

        try
        {
            _logger.LogInformation("Starting");
            
            RegisterEventHandlers();

            ServiceStarting?.Invoke(this, new ServiceEventArguments(cancellationToken));

            await LoadUsersAsync(cancellationToken);

            while (await periodicTimer.WaitForNextTickAsync(cancellationToken))
                await UpdateBroadcastStatesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Exiting");

            ServiceExiting?.Invoke(this, new ServiceEventArguments(cancellationToken));

            await _data.SaveUsersAsync(_users.Values, CancellationToken.None);
            UnregisterEventHandlers();
        }
        finally
        {
            _logger.LogInformation("Exited");

            ServiceExited?.Invoke(this, new ServiceEventArguments(CancellationToken.None));
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

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Started");

        ServiceStarted?.Invoke(this, new ServiceEventArguments(cancellationToken));

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateBroadcastStatesAsync(CancellationToken cancellationToken)
    {
        var userBatches = _users.Values
            .Select(user => user.UserData.Id)
            .Chunk(_settings.MaxUsersPerRequest);

        foreach (var batch in userBatches)
        {
            var detectedLive = new List<TwitchStreamer>();
            var endedBroadcasts = new List<TwitchStreamer>();
            var continuingBroadcasts = new List<TwitchStreamer>();
            var refreshDue = new List<TwitchStreamer>();

            try
            {
                var (twitchStreams, _) = await _streamsModule.GetStreamsAsync(
                    new TwitchStreamRequest
                    {
                        UserIds = [.. batch],
                        Type = "live"
                    }, cancellationToken);

                var tasks = batch.Select(async userId =>
                {
                    if (_users.TryGetValue(userId, out var user))
                    {
                        user.StreamData = twitchStreams
                            .FirstOrDefault(stream => stream.UserId == userId);

                        await UpdateUserStateAsync(
                            user, detectedLive, endedBroadcasts,
                            continuingBroadcasts, refreshDue);
                    }
                });

                await Task.WhenAll(tasks);

                if (detectedLive.Count != 0)
                {
                    var args = new UsersEventArguments(cancellationToken) { Users = detectedLive };
                    BroadcastDetectedLive?.Invoke(this, args);
                }

                if (endedBroadcasts.Count != 0)
                {
                    var args = new UsersEventArguments(cancellationToken) { Users = endedBroadcasts };
                    BroadcastEnded?.Invoke(this, args);
                }

                if (continuingBroadcasts.Count != 0)
                {
                    var args = new UsersEventArguments(cancellationToken) { Users = continuingBroadcasts };
                    BroadcastContinuing?.Invoke(this, args);
                }

                if (refreshDue.Count != 0)
                {
                    var args = new UsersEventArguments(cancellationToken) { Users = refreshDue };
                    BroadcastMediaRefreshDue?.Invoke(this, args);
                }
            }
            catch (Exception exception)
            {
                foreach (var userId in batch)
                {
                    var args = new ErrorEventArguments(userId, _logMessages.Errors.WasNotUpdated, exception);
                    UserBroadcastError?.Invoke(this, args);
                }
            }
        }
        
        await _data.SaveUsersAsync(_users.Values, cancellationToken);

        _logger.LogDebug("{LogMessage}", _logMessages.UserWasSaved);
    }

    private async Task UpdateUserStateAsync(
        TwitchStreamer user,
        List<TwitchStreamer> detectedLive,
        List<TwitchStreamer> endedBroadcasts,
        List<TwitchStreamer> continuingBroadcasts,
        List<TwitchStreamer> refreshDue)
    {
        bool isOnline = user.StreamData != null;
        var now = DateTime.UtcNow;

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

    public async Task<UserManagementResult> AddUserAsync(
        string userLogin, 
        CancellationToken cancellationToken)
    {
        var user = await _usersModule.GetUserAsync(userLogin, cancellationToken);

        if (user == null)
        {
            _logger.LogWarning(
                "{LogMessage} ({Data})", 
                _logMessages.Errors.UserWasNotFound, 
                userLogin);

            return UserManagementResult.NotFound;
        }

        var twitchStreamer = new TwitchStreamer
        {
            UserData = user,
            StreamData = null,
            LastOnline = null,
            NextMediaRefresh = null
        };

        if (_users.TryAdd(user.Id, twitchStreamer))
        {
            var args = new UsersEventArguments(cancellationToken) { Users = [twitchStreamer] };
            UserAdded?.Invoke(this, args);
            
            await _data.SaveUsersAsync(_users.Values, cancellationToken);

            _logger.LogDebug(
                "{LogMessage} ({Data})", 
                _logMessages.UserWasSaved, 
                userLogin);
            
            return UserManagementResult.Success;
        }
        else
        {
            _logger.LogWarning("{LogMessage} ({Data})", _logMessages.Errors.UserExists, user.Id);
            return UserManagementResult.AlreadyExists;
        }
    }

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
            _logger.LogWarning(
                "{LogMessage} ({Data})",
                _logMessages.Errors.UserWasNotFound,
                userLogin);

            return UserManagementResult.NotFound;
        }

        if (_users.TryRemove(userId, out var user))
        {
            var args = new UsersEventArguments(cancellationToken) { Users = [user] };
            UserRemoved?.Invoke(this, args);

            await _data.SaveUsersAsync(_users.Values, cancellationToken);
            return UserManagementResult.Success;
        }

        _logger.LogError(
            "{LogMessage} ({Data})", 
            _logMessages.Errors.UserWasNotRemoved, 
            userId);

        return UserManagementResult.Error;
    }

    private async Task LoadUsersAsync(CancellationToken cancellationToken)
    {
        foreach (var user in await _data.LoadUsersAsync(cancellationToken))
            _users.TryAdd(user.UserData.Id, user);
    }
}
