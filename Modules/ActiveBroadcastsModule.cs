using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using TvGuide.Models;

namespace TvGuide.Modules;

/// <summary>
/// Manages a single Discord message containing all active Twitch broadcasts using Components V2.
/// </summary>
/// <remarks>
/// <para><b>Architecture:</b></para>
/// <list type="bullet">
/// <item>ONE Discord message contains ALL active broadcasts</item>
/// <item>Each broadcast = 1 ComponentContainer (up to 10 max)</item>
/// <item>Summary info at bottom as components (not containerized)</item>
/// <item>Discord limits: 10 containers max, 40 total components max</item>
/// </list>
/// 
/// <para><b>Offline Handling:</b></para>
/// When a stream ends, the offline broadcast stays in the old message.
/// A NEW message is created with only the remaining active broadcasts.
/// This eliminates complex position management and message swapping.
/// 
/// <para><b>Persistence:</b></para>
/// Broadcast data (without Discord component objects) is saved to JSON.
/// On restart, LoadDataAsync restores broadcast list and message ID.
/// Components are rebuilt from persisted user/stream snapshots on each update.
/// </remarks>
public sealed class ActiveBroadcastsModule(
    IOptions<Configuration> settings,
    ILogger<ActiveBroadcastsModule> logger,
    RestClient restClient) : IDisposable
{
    private readonly RestClient _restClient = restClient;
    private readonly Settings.NowLive _settings = settings.Value.NowLive;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ILogger<ActiveBroadcastsModule> _logger = logger;
    private ActiveBroadcastsState _activeBroadcasts = new();

    /// <summary>
    /// Loads persisted broadcast data from the configured file into memory.
    /// If the file does not exist or is empty, initializes an empty broadcast list.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the load operation.</param>
    public async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settings.ActiveBroadcastsFile))
            {
                _activeBroadcasts = new();
                ActiveBroadcastsModuleLog.ActiveBroadcastFileNotFound(_logger, _settings.ActiveBroadcastsFile);
                return;
            }

            await using var fileStream = File.OpenRead(_settings.ActiveBroadcastsFile);
            if (fileStream.Length == 0)
            {
                _activeBroadcasts = new();
                return;
            }

            _activeBroadcasts = await JsonSerializer
                .DeserializeAsync<ActiveBroadcastsState>(fileStream, cancellationToken: cancellationToken)
                .ConfigureAwait(false) ?? new();

            _activeBroadcasts.ActiveBroadcasts ??= [];
            _activeBroadcasts.ActiveBroadcasts =
            [
                .. _activeBroadcasts.ActiveBroadcasts
                    .Where(broadcast => !string.IsNullOrEmpty(broadcast.UserData.Id))
                    .OrderBy(broadcast => broadcast.StreamData?.StartedAt ?? DateTimeOffset.MaxValue)
            ];

            ActiveBroadcastsModuleLog.BroadcastsLoaded(_logger, _activeBroadcasts.ActiveBroadcasts.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            ActiveBroadcastsModuleLog.FailedToLoadBroadcastData(
                _logger,
                exception,
                _settings.ActiveBroadcastsFile);
            _activeBroadcasts = new();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Persists the current broadcast data to the configured file.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the save operation.</param>
    private async Task SaveDataAsync(CancellationToken cancellationToken)
    {
        await using var fileStream = File.Create(_settings.ActiveBroadcastsFile);
        await JsonSerializer.SerializeAsync(
            fileStream,
            _activeBroadcasts,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures the status message is rebuilt on startup to sync with current state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the rebuild operation.</param>
    public async Task EnsureStatusMessageExistsAsync(CancellationToken cancellationToken) 
        => await RebuildBroadcastMessageAsync(cancellationToken);

    /// <summary>
    /// Adds a new broadcast and rebuilds the single Discord message.
    /// </summary>
    /// <param name="twitchStreamer">Streamer data to add to the active broadcast message.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    public async Task CreateBroadcastMessageAsync(TrackedTwitchUser twitchStreamer, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            ArgumentNullException.ThrowIfNull(twitchStreamer.StreamData);

            _activeBroadcasts.ActiveBroadcasts.Add(new ActiveBroadcastEntry
            {
                UserData = twitchStreamer.UserData,
                StreamData = twitchStreamer.StreamData,
                LastUpdated = DateTimeOffset.UtcNow
            });

            _activeBroadcasts.ActiveBroadcasts =
            [
                .. _activeBroadcasts.ActiveBroadcasts
                    .OrderBy(broadcast => broadcast.StreamData?.StartedAt ?? DateTimeOffset.MaxValue)
            ];

            if (_activeBroadcasts.ActiveBroadcasts.Count > 10)
            {
                ActiveBroadcastsModuleLog.ExceededBroadcastLimit(_logger);

                _activeBroadcasts.ActiveBroadcasts.RemoveAt(_activeBroadcasts.ActiveBroadcasts.Count - 1);
            }

            await RebuildBroadcastMessageAsync(cancellationToken);
        }
        finally { _semaphore.Release(); }
    }

    /// <summary>
    /// Removes a broadcast and creates a new Discord message for remaining active broadcasts.
    /// The old message is left with the offline broadcast.
    /// </summary>
    /// <param name="twitchStreamer">Streamer data identifying the broadcast to remove.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    public async Task RemoveBroadcastMessageAsync(TrackedTwitchUser twitchStreamer, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var broadcastToRemove = _activeBroadcasts.ActiveBroadcasts
                .FirstOrDefault(broadcast => broadcast.UserData.Id == twitchStreamer.UserData.Id);

            if (broadcastToRemove == null)
            {
                ActiveBroadcastsModuleLog.BroadcastNotFoundForRemoval(
                    _logger,
                    twitchStreamer.UserData.DisplayName,
                    twitchStreamer.UserData.Id);

                return;
            }

            _activeBroadcasts.ActiveBroadcasts.Remove(broadcastToRemove);

            var oldMessageId = _activeBroadcasts.ActiveBroadcastsMessageId;

            if (oldMessageId != 0)
            {
                try
                {
                    var offlineContainer = CreateOfflineContainer(broadcastToRemove);
                    await _restClient.ModifyMessageAsync(
                        _settings.ChannelId,
                        oldMessageId,
                        message => message
                            .WithComponents([offlineContainer])
                            .WithFlags(MessageFlags.IsComponentsV2),
                        cancellationToken: cancellationToken);
                }
                catch (RestException restException) when (restException.StatusCode == HttpStatusCode.NotFound)
                {
                    ActiveBroadcastsModuleLog.OldBroadcastMessageNotFound(_logger);
                }

                // Reset message ID so RebuildBroadcastMessageAsync creates a new message
                _activeBroadcasts.ActiveBroadcastsMessageId = 0;
            }

            await RebuildBroadcastMessageAsync(cancellationToken);
        }
        finally { _semaphore.Release(); }
    }

    /// <summary>
    /// Updates broadcast data and rebuilds the single Discord message.
    /// </summary>
    /// <param name="twitchStreamer">Streamer data containing the latest broadcast state.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    public async Task UpdateBroadcastMessageAsync(
        TrackedTwitchUser twitchStreamer,
        CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            ArgumentNullException.ThrowIfNull(twitchStreamer.StreamData);

            var broadcast = _activeBroadcasts.ActiveBroadcasts
                .FirstOrDefault(activeBroadcast => activeBroadcast.UserData.Id == twitchStreamer.UserData.Id);

            if (broadcast == null)
            {
                ActiveBroadcastsModuleLog.BroadcastNotFoundForUpdate(_logger, twitchStreamer.UserData.Id);
                return;
            }

            broadcast.UserData = twitchStreamer.UserData;
            broadcast.StreamData = twitchStreamer.StreamData;
            broadcast.LastUpdated = DateTimeOffset.UtcNow;

            _activeBroadcasts.ActiveBroadcasts =
            [
                .. _activeBroadcasts.ActiveBroadcasts
                    .OrderBy(activeBroadcast => activeBroadcast.StreamData?.StartedAt ?? DateTimeOffset.MaxValue)
            ];

            await RebuildBroadcastMessageAsync(cancellationToken);
        }
        finally { _semaphore.Release(); }
    }

    /// <summary>
    /// Rebuilds the entire broadcast message with all active broadcasts and summary.
    /// Creates a new message or modifies the existing message stored in <see cref="ActiveBroadcastsState.ActiveBroadcastsMessageId"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the rebuild operation.</param>
    private async Task RebuildBroadcastMessageAsync(CancellationToken cancellationToken)
    {
        try
        {
            var components = new List<IMessageComponentProperties>();

            foreach (var broadcast in _activeBroadcasts.ActiveBroadcasts
                .Where(activeBroadcast => activeBroadcast.StreamData != null)
                .Take(10))
            {
                var container = CreateBroadcastContainer(broadcast);
                components.Add(container);
            }

            components.AddRange(CreateSummaryComponents());

            if (components.Count > 40)
            {
                ActiveBroadcastsModuleLog.ComponentCountExceeded(_logger, components.Count);

                components = [.. components.Take(40)];
            }

            if (_activeBroadcasts.ActiveBroadcastsMessageId == 0)
            {
                var newMessage = await _restClient.SendMessageAsync(
                    _settings.ChannelId,
                    new MessageProperties()
                        .WithComponents(components)
                        .WithFlags(MessageFlags.IsComponentsV2),
                    cancellationToken: cancellationToken);

                _activeBroadcasts.ActiveBroadcastsMessageId = newMessage.Id;
            }
            else
            {
                await _restClient.ModifyMessageAsync(
                    _settings.ChannelId,
                    _activeBroadcasts.ActiveBroadcastsMessageId,
                    message => message
                        .WithComponents(components)
                        .WithFlags(MessageFlags.IsComponentsV2),
                    cancellationToken: cancellationToken);
            }

            await SaveDataAsync(cancellationToken);
        }
        catch (RestException restException) when (restException.StatusCode == HttpStatusCode.NotFound)
        {
            ActiveBroadcastsModuleLog.BroadcastMessageNotFound(_logger);

            _activeBroadcasts.ActiveBroadcastsMessageId = 0;
            await RebuildBroadcastMessageAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Checks if a broadcast is already tracked.
    /// </summary>
    /// <param name="twitchStreamer">Streamer data to locate in the tracked broadcast list.</param>
    /// <returns><see langword="true"/> when the streamer already has a tracked message entry; otherwise, <see langword="false"/>.</returns>
    public bool IsMessageTracked(TrackedTwitchUser twitchStreamer) =>
        _activeBroadcasts.ActiveBroadcasts.Any(broadcast => broadcast.UserData.Id == twitchStreamer.UserData.Id);

    /// <summary>
    /// Creates a component container representing an active broadcast. The container includes
    /// the streamer's profile thumbnail, the stream preview image, title, game, viewer count
    /// and a link button to watch the stream.
    /// </summary>
    /// <param name="broadcast">Broadcast data for the streamer.</param>
    /// <returns>A configured <see cref="ComponentContainerProperties"/> representing the broadcast.</returns>
    private ComponentContainerProperties CreateBroadcastContainer(ActiveBroadcastEntry broadcast)
    {
        ArgumentNullException.ThrowIfNull(broadcast.StreamData);

        var streamUrl = $"https://www.twitch.tv/{broadcast.UserData.Login}";
        var previewUrl = GetStreamPreviewUrl(broadcast.UserData.Login).Replace("{width}x{height}", "1920x1080");

        var container = new ComponentContainerProperties()
            .WithAccentColor(new NetCord.Color(_settings.StatusColor.Online))
            .AddComponents(
                new ComponentSectionProperties(
                    new ComponentSectionThumbnailProperties(new ComponentMediaProperties(broadcast.UserData.ProfileImageUrl)))
                    .AddComponents(
                        new TextDisplayProperties($"## [{broadcast.UserData.DisplayName} is now live!](https://www.twitch.tv/{broadcast.UserData.Login})\n{broadcast.StreamData.Title}")
                    ),
                new ComponentSeparatorProperties().WithDivider(false),
                new MediaGalleryProperties()
                    .AddItems(new MediaGalleryItemProperties(new ComponentMediaProperties(previewUrl))),
                new ComponentSectionProperties(
                    new LinkButtonProperties(streamUrl, "Watch Stream"))
                    .AddComponents(
                        new TextDisplayProperties($"🎮 {broadcast.StreamData.GameName} | 👥 {broadcast.StreamData.ViewerCount} viewers | Started <t:{broadcast.StreamData.StartedAt.ToUnixTimeSeconds()}:R>")
                    )
            );

        return container;
    }

    /// <summary>
    /// Creates a compact container representing an offline or finished broadcast.
    /// The container shows only a linked username and the stream duration.
    /// </summary>
    /// <param name="broadcast">Broadcast data for the finished stream.</param>
    /// <returns>A configured <see cref="ComponentContainerProperties"/> for the offline broadcast.</returns>
    private ComponentContainerProperties CreateOfflineContainer(ActiveBroadcastEntry broadcast)
    {
        ArgumentNullException.ThrowIfNull(broadcast.StreamData);

        var duration = FormatDuration(DateTimeOffset.UtcNow - broadcast.StreamData.StartedAt);

        return new ComponentContainerProperties()
            .WithAccentColor(new NetCord.Color(_settings.StatusColor.Offline))
            .AddComponents(
                new TextDisplayProperties($"## [{broadcast.UserData.DisplayName}](https://www.twitch.tv/{broadcast.UserData.Login}) finished streaming\nStream Duration: {duration}"));
    }

    /// <summary>
    /// Creates summary components (not containerized) shown at the bottom of the message.
    /// When no streams are active, the summary indicates that and still includes a "last checked" timestamp.
    /// </summary>
    /// <returns>A list of components to append to the broadcast message.</returns>
    private List<IMessageComponentProperties> CreateSummaryComponents()
    {
        var components = new List<IMessageComponentProperties>();

        if (_activeBroadcasts.ActiveBroadcasts.Count == 0)
        {
            components.Add(new ComponentSeparatorProperties());
            components.Add(new TextDisplayProperties($"## Active Streams\nNo streams are currently active"));
        }
        else
        {
            components.Add(new ComponentSeparatorProperties());
            var streamerList = string.Join("\n", _activeBroadcasts.ActiveBroadcasts
                .Select(b => $"• [{b.UserData.DisplayName}](https://www.twitch.tv/{b.UserData.Login})"));
            var lastUpdated = _activeBroadcasts.ActiveBroadcasts.Max(b => b.LastUpdated);
            components.Add(new TextDisplayProperties($"## Active Streams\n{_activeBroadcasts.ActiveBroadcasts.Count} stream(s) currently active\n\n{streamerList}\n\nLast updated <t:{lastUpdated.ToUnixTimeSeconds()}:R>"));
        }

        return components;
    }

    /// <summary>
    /// Formats duration to human-readable string.
    /// </summary>
    /// <param name="duration">Duration to format.</param>
    /// <returns>A human-readable duration string using minutes and hours.</returns>
    public static string FormatDuration(TimeSpan duration)
    {
        static string Pluralize(int value, string singular) =>
            $"{value} {singular}{(value == 1 ? "" : "s")}";

        if (duration.TotalMinutes < 60)
            return Pluralize((int)duration.TotalMinutes, "minute");

        var hours = (int)duration.TotalHours;
        var minutes = duration.Minutes;
        var hoursText = Pluralize(hours, "hour");

        return minutes > 0
            ? $"{hoursText} and {Pluralize(minutes, "minute")}"
            : hoursText;
    }

    /// <summary>
    /// Generates stream preview URL with cache-busting timestamp.
    /// </summary>
    /// <param name="userLogin">Twitch login name used in the preview-image path.</param>
    /// <param name="width">Requested preview width.</param>
    /// <param name="height">Requested preview height.</param>
    /// <returns>A Twitch CDN preview-image URL with a cache-busting timestamp.</returns>
    public static string GetStreamPreviewUrl(string userLogin, int width = 1280, int height = 720) =>
        $"https://static-cdn.jtvnw.net/previews-ttv/live_user_{userLogin}-{width}x{height}.jpg?{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

    /// <summary>
    /// Releases the semaphore used to serialize broadcast-message updates.
    /// </summary>
    public void Dispose() => _semaphore.Dispose();
}
