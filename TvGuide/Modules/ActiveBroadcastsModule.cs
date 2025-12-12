using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;

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
/// Components are rebuilt from TwitchUser/TwitchStream data on each update.
/// </remarks>
public sealed class ActiveBroadcastsModule(
    IOptions<Configuration> settings,
    ILogger<ActiveBroadcastsModule> logger,
    RestClient restClient,
    HttpClient httpClient) : IDisposable
{
    private readonly RestClient _restClient = restClient;
    private readonly HttpClient _httpClient = httpClient;
    private readonly Settings.NowLive _settings = settings.Value.NowLive;
    private readonly LogMessages _logMessages = settings.Value.LogMessages;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ILogger<ActiveBroadcastsModule> _logger = logger;
    private Broadcasts _activeBroadcasts = new();

    public async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settings.ActiveBroadcastsFile))
            {
                _activeBroadcasts = new();
                _logger.LogInformation(
                    "{LogMessage} ({Data})",
                    _logMessages.Errors.FileNotFound,
                    _settings.ActiveBroadcastsFile);
                return;
            }

            await using var fileStream = File.OpenRead(_settings.ActiveBroadcastsFile);
            if (fileStream.Length == 0)
            {
                _activeBroadcasts = new();
                return;
            }

            _activeBroadcasts = await JsonSerializer
                .DeserializeAsync<Broadcasts>(fileStream, cancellationToken: cancellationToken)
                .ConfigureAwait(false) ?? new();

            _activeBroadcasts.ActiveBroadcasts ??= [];

            _logger.LogInformation("{LogMessage} - {Count} broadcast(s) loaded",
                _logMessages.DataWasLoaded, _activeBroadcasts.ActiveBroadcasts.Count);
        }
        catch (OperationCanceledException) 
        { 
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "{LogMessage} ({Data})",
                _logMessages.Errors.DataWasNotLoaded, _settings.ActiveBroadcastsFile);
            _activeBroadcasts = new();
        }
        finally 
        { 
            _semaphore.Release(); 
        }
    }

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
    public async Task EnsureStatusMessageExistsAsync(CancellationToken cancellationToken)
    {
        await RebuildBroadcastMessageAsync(cancellationToken);
    }

    /// <summary>
    /// Adds a new broadcast and rebuilds the single Discord message.
    /// </summary>
    public async Task CreateBroadcastMessageAsync(TwitchStreamer twitchStreamer, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            ArgumentNullException.ThrowIfNull(twitchStreamer.StreamData);

            _activeBroadcasts.ActiveBroadcasts.Insert(0, new Broadcasts.BroadcastData
            {
                UserData = twitchStreamer.UserData,
                StreamData = twitchStreamer.StreamData,
                LastUpdated = DateTime.UtcNow
            });

            if (_activeBroadcasts.ActiveBroadcasts.Count > 10)
            {
                _logger.LogWarning("Exceeded 10 broadcast limit, removing oldest");
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
    public async Task RemoveBroadcastMessageAsync(TwitchStreamer twitchStreamer, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var broadcastToRemove = _activeBroadcasts.ActiveBroadcasts
                .FirstOrDefault(b => b.UserData.Id == twitchStreamer.UserData.Id);

            if (broadcastToRemove == null)
            {
                _logger.LogWarning(
                    "Broadcast for {DisplayName} ({Id}) not found, skipping removal",
                    twitchStreamer.UserData.DisplayName, twitchStreamer.UserData.Id);
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
                    _logger.LogWarning("Old broadcast message not found, cannot update to offline");
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
    public async Task UpdateBroadcastMessageAsync(
        TwitchStreamer twitchStreamer,
        CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            ArgumentNullException.ThrowIfNull(twitchStreamer.StreamData);

            var broadcast = _activeBroadcasts.ActiveBroadcasts
                .FirstOrDefault(b => b.UserData.Id == twitchStreamer.UserData.Id);

            if (broadcast == null)
            {
                _logger.LogWarning("Broadcast not found for user {UserId}, cannot update", twitchStreamer.UserData.Id);
                return;
            }

            broadcast.UserData = twitchStreamer.UserData;
            broadcast.StreamData = twitchStreamer.StreamData;
            broadcast.LastUpdated = DateTime.UtcNow;

            await RebuildBroadcastMessageAsync(cancellationToken);
        }
        finally { _semaphore.Release(); }
    }

    /// <summary>
    /// Rebuilds the entire broadcast message with all active broadcasts and summary.
    /// Creates a new message or modifies existing one.
    /// </summary>
    private async Task RebuildBroadcastMessageAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Build all containers and components
            var components = new List<IMessageComponentProperties>();

            // Add broadcast containers (max 10)
            foreach (var broadcast in _activeBroadcasts.ActiveBroadcasts.Take(10))
            {
                if (broadcast.StreamData == null)
                {
                    _logger.LogWarning("Skipping broadcast for {User} - StreamData is null", broadcast.UserData.Login);
                    continue;
                }

                var container = CreateBroadcastContainer(broadcast);
                components.Add(container);
            }

            // Add summary components (not containerized)
            components.AddRange(CreateSummaryComponents());

            // Validate component count
            if (components.Count > 40)
            {
                _logger.LogWarning("Component count {Count} exceeds 40 limit, truncating", components.Count);
                components = components.Take(40).ToList();
            }

            // Create or modify message
            if (_activeBroadcasts.ActiveBroadcastsMessageId == 0)
            {
                // Create new message
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
                // Modify existing message
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
            _logger.LogWarning("Broadcast message not found, recreating");
            _activeBroadcasts.ActiveBroadcastsMessageId = 0;
            await RebuildBroadcastMessageAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Checks if a broadcast is already tracked.
    /// </summary>
    public bool IsMessageTracked(TwitchStreamer twitchStreamer) => 
        _activeBroadcasts.ActiveBroadcasts.Any(b => b.UserData.Id == twitchStreamer.UserData.Id);

    /// <summary>
    /// Creates a broadcast container with preview image attachment.
    /// </summary>
    private ComponentContainerProperties CreateBroadcastContainer(Broadcasts.BroadcastData broadcast)
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
                        new TextDisplayProperties($"🎮 {broadcast.StreamData.GameName} | 👥 {broadcast.StreamData.ViewerCount} viewers | Started <t:{new DateTimeOffset(broadcast.StreamData.StartedAt).ToUnixTimeSeconds()}:R>")
                    )
            );

        return container;
    }

    /// <summary>
    /// Creates an offline container for a finished broadcast.
    /// </summary>
    private ComponentContainerProperties CreateOfflineContainer(Broadcasts.BroadcastData broadcast)
    {
        ArgumentNullException.ThrowIfNull(broadcast.StreamData);

        var duration = FormatDuration(DateTime.UtcNow - broadcast.StreamData.StartedAt);
        var section = new ComponentSectionProperties(
            new LinkButtonProperties($"https://www.twitch.tv/{broadcast.UserData.Login}", "View Channel"))
            .AddComponents(
                new TextDisplayProperties($"**{broadcast.UserData.DisplayName} finished streaming**"),
                new TextDisplayProperties($"Stream Duration: {duration}")
            );

        return new ComponentContainerProperties()
            .WithAccentColor(new NetCord.Color(_settings.StatusColor.Offline))
            .AddComponents(section);
    }

    /// <summary>
    /// Creates summary components (not containerized) to show at the bottom.
    /// </summary>
    private List<IMessageComponentProperties> CreateSummaryComponents()
    {
        var components = new List<IMessageComponentProperties>();
        var lastChecked = DateTime.UtcNow;

        if (_activeBroadcasts.ActiveBroadcasts.Count == 0)
        {
            components.Add(new ComponentSeparatorProperties());
            components.Add(new TextDisplayProperties($"## Active Streams\nNo streams are currently active\n\nLast checked <t:{new DateTimeOffset(lastChecked).ToUnixTimeSeconds()}:R>"));
        }
        else
        {
            components.Add(new ComponentSeparatorProperties());
            var streamerList = string.Join("\n", _activeBroadcasts.ActiveBroadcasts
                .Select(b => $"• [{b.UserData.DisplayName}](https://www.twitch.tv/{b.UserData.Login})"));
            var lastUpdated = _activeBroadcasts.ActiveBroadcasts.Max(b => b.LastUpdated);
            components.Add(new TextDisplayProperties($"## Active Streams\n{_activeBroadcasts.ActiveBroadcasts.Count} stream(s) currently active\n\n{streamerList}\n\nLast updated <t:{new DateTimeOffset(lastUpdated).ToUnixTimeSeconds()}:R>"));
        }

        return components;
    }

    /// <summary>
    /// Formats duration to human-readable string.
    /// </summary>
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
    public static string GetStreamPreviewUrl(string userLogin, int width = 1280, int height = 720) => 
        $"https://static-cdn.jtvnw.net/previews-ttv/live_user_{userLogin}-{width}x{height}.jpg?{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

    public void Dispose() => _semaphore.Dispose();
}

/// <summary>
/// Active broadcasts tracked in a single Discord message.
/// </summary>
/// <remarks>
/// Discord Components V2 limits: 10 containers max, 40 total components max per message.
/// Each broadcast = 1 container. Summary = additional components (not containerized).
/// </remarks>
public sealed class Broadcasts
{
    /// <summary>
    /// Discord message ID containing all active broadcasts.
    /// </summary>
    public ulong ActiveBroadcastsMessageId { get; set; }
    
    /// <summary>
    /// Active broadcast data.
    /// </summary>
    public List<BroadcastData> ActiveBroadcasts { get; set; } = [];

    /// <summary>
    /// Broadcast data for a single streamer.
    /// </summary>
    public sealed class BroadcastData
    {
        public required TwitchUser UserData { get; set; }
        public required TwitchStream? StreamData { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
