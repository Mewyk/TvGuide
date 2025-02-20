using System.Collections.Concurrent;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using TvGuide.Modules;
using TvGuide.Events;

namespace TvGuide.Services;

public class NowLiveService(
    DataModule data,
    IStreamsModule streamsModule,
    IUsersModule usersModule,
    IOptions<Configuration> settings,
    ILogger<NowLiveService> logger,
    BroadcastStates broadcastState
) : BackgroundService, INowLiveService
{
    private readonly IStreamsModule _nowLiveService = streamsModule;
    private readonly IUsersModule _usersModule = usersModule;
    private readonly ILogger<NowLiveService> _logger = logger;
    private readonly DataModule _data = data;
    private readonly Settings.NowLive _settings = settings.Value.NowLive;
    private readonly LogMessages _logMessages = settings.Value.LogMessages;
    private readonly ConcurrentDictionary<string, TwitchStreamer> _users = new();
    private readonly BroadcastStates _broadcastState = broadcastState;

    // Bulk event handlers
    public event EventHandler<UsersEventArguments>? BroadcastStateOnline;
    public event EventHandler<UsersEventArguments>? BroadcastStateOffline;
    public event EventHandler<UsersEventArguments>? BroadcastStateUnchanged;
    public event EventHandler<UsersEventArguments>? BroadcastStateMediaRefresh;

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

            //TODO: Make better use of the Starting vs Started
            ServiceStarting?.Invoke(this, new ServiceEventArguments 
            { 
                CancellationToken = cancellationToken 
            });

            await LoadUsersAsync(cancellationToken);

            while (await periodicTimer.WaitForNextTickAsync(cancellationToken))
                await UpdateBroadcastStatesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Exiting");

            ServiceExiting?.Invoke(this, new ServiceEventArguments 
            { 
                CancellationToken = cancellationToken 
            });

            await _data.SaveUsersAsync(_users.Values, CancellationToken.None);
            UnregisterEventHandlers();
        }
        finally
        {
            _logger.LogInformation("Exited");

            ServiceExited?.Invoke(this, new ServiceEventArguments 
            { 
                CancellationToken = CancellationToken.None 
            });
        }
    }

    private void RegisterEventHandlers()
    {
        BroadcastStateOnline += _broadcastState.OnBroadcastStateOnline;
        BroadcastStateOffline += _broadcastState.OnBroadcastStateOffline;
        BroadcastStateUnchanged += _broadcastState.OnBroadcastStateUnchanged;
        BroadcastStateMediaRefresh += _broadcastState.OnBroadcastStateMediaRefresh;

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
        BroadcastStateOnline -= _broadcastState.OnBroadcastStateOnline;
        BroadcastStateOffline -= _broadcastState.OnBroadcastStateOffline;
        BroadcastStateUnchanged -= _broadcastState.OnBroadcastStateUnchanged;
        BroadcastStateMediaRefresh -= _broadcastState.OnBroadcastStateMediaRefresh;

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

        //TODO: Make better use of the Starting vs Started
        ServiceStarted?.Invoke(this, 
            new ServiceEventArguments { CancellationToken = cancellationToken });

        await base.StartAsync(cancellationToken);
    }

    public override void Dispose()
    {
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task UpdateBroadcastStatesAsync(CancellationToken cancellationToken)
    {
        var userBatches = _users.Values
            .Select(user => user.UserData.Id)
            .Chunk(_settings.MaxUsersPerRequest);

        foreach (var batch in userBatches)
        {
            var onlineUsers = new List<TwitchStreamer>();
            var offlineUsers = new List<TwitchStreamer>();
            var unchangedUsers = new List<TwitchStreamer>();
            var mediaRefreshUsers = new List<TwitchStreamer>();

            try
            {
                var (TwitchStreams, NextCursor) = await _nowLiveService.GetStreamsAsync(
                    new TwitchStreamRequest
                    {
                        UserIds = [.. batch],
                        Type = "live"
                    }, cancellationToken);

                var liveUserIds = TwitchStreams.Select(twitchStream => twitchStream.UserId);

                var tasks = batch.Select(async userId =>
                {
                    if (_users.TryGetValue(userId, out var user))
                    {
                        user.StreamData = TwitchStreams
                            .FirstOrDefault(User => User.UserId == userId);

                        await UpdateUserStateAsync(
                            user, onlineUsers, offlineUsers,
                            unchangedUsers, mediaRefreshUsers);
                    }
                });

                await Task.WhenAll(tasks);

                if (onlineUsers.Count != 0)
                    BroadcastStateOnline?.Invoke(this, new UsersEventArguments
                    {
                        Users = onlineUsers,
                        CancellationToken = cancellationToken
                    });

                if (offlineUsers.Count != 0)
                    BroadcastStateOffline?.Invoke(this, new UsersEventArguments
                    {
                        Users = offlineUsers,
                        CancellationToken = cancellationToken
                    });

                if (unchangedUsers.Count != 0)
                    BroadcastStateUnchanged?.Invoke(this, new UsersEventArguments
                    {
                        Users = unchangedUsers,
                        CancellationToken = cancellationToken
                    });

                if (mediaRefreshUsers.Count != 0)
                    BroadcastStateMediaRefresh?.Invoke(this, new UsersEventArguments
                    {
                        Users = mediaRefreshUsers,
                        CancellationToken = cancellationToken
                    });
            }
            catch (Exception exception)
            {
                foreach (var userId in batch)
                    UserBroadcastError?.Invoke(this, new ErrorEventArguments
                    {
                        UserId = userId,
                        Message = _logMessages.Errors.WasNotUpdated,
                        Exception = exception
                    });
            }
        }
        
        await _data.SaveUsersAsync(_users.Values, cancellationToken);

        _logger.LogDebug("{LogMessage}", _logMessages.UserWasSaved);
    }

    private async Task UpdateUserStateAsync(
        TwitchStreamer user,
        List<TwitchStreamer> onlineUsers,
        List<TwitchStreamer> offlineUsers,
        List<TwitchStreamer> unchangedUsers,
        List<TwitchStreamer> mediaRefreshUsers)
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
                onlineUsers.Add(user);
            }
            else
            {
                if (user.NextMediaRefresh.HasValue && now >= user.NextMediaRefresh.Value)
                {
                    mediaRefreshUsers.Add(user);
                    user.NextMediaRefresh = now.AddMinutes(_settings.MediaRefreshInterval);
                }
                else unchangedUsers.Add(user);
            }
        }
        else if (user.IsLive)
        {
            user.IsLive = false;
            user.LastOnline = null;
            user.NextMediaRefresh = null;
            offlineUsers.Add(user);
        }

        await Task.CompletedTask;
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
            UserAdded?.Invoke(this, new UsersEventArguments 
            { 
                Users = [twitchStreamer], 
                CancellationToken = cancellationToken 
            });
            
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
            UserRemoved?.Invoke(this, new UsersEventArguments
            { 
                Users = [user], 
                CancellationToken = cancellationToken 
            });

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
