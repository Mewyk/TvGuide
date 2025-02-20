using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord.Rest;

namespace TvGuide.Modules;

public sealed class ActiveBroadcastsModule(
    IOptions<Configuration> settings,
    ILogger<ActiveBroadcastsModule> logger,
    RestClient restClient)
{
    private readonly RestClient _restClient = restClient;
    private readonly Settings.NowLive _settings = settings.Value.NowLive;
    private readonly LogMessages _logMessages = settings.Value.LogMessages;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly string _applicationName = settings.Value.ApplicationName;
    private readonly string _applicationVersion = settings.Value.ApplicationVersion;
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

            _activeBroadcasts.Messages ??= [];

            _logger.LogInformation(
                "{LogMessage} ({Data})",
                _logMessages.DataWasLoaded,
                _settings.ActiveBroadcastsFile);
        }
        catch (OperationCanceledException) { throw; /* TODO: Handle cancellation */ }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "{LogMessage} ({Data})",
                _logMessages.Errors.DataWasNotLoaded,
                _settings.ActiveBroadcastsFile);
            _activeBroadcasts = new();
        }
        finally { _semaphore.Release(); }
    }

    private async Task SaveDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var fileStream = File.Create(_settings.ActiveBroadcastsFile);
            await JsonSerializer.SerializeAsync(
                fileStream,
                _activeBroadcasts,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "{LogMessage} ({Data})",
                _logMessages.Errors.JsonWasNotProcessed,
                _settings.ActiveBroadcastsFile);
            throw;
        }
    }

    public async Task ProcessOnlineStateAsync(TwitchStreamer twitchStreamer, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            foreach (var existingMessage in _activeBroadcasts.Messages)
                existingMessage.Position += 1;

            var newUserMessage = new Broadcasts.Message
            {
                Id = _activeBroadcasts.SummaryMessageId,
                Position = 1,
                UserData = twitchStreamer.UserData,
                StreamData = twitchStreamer.StreamData
                    ?? throw new ArgumentNullException(nameof(twitchStreamer)),
                UserEmbed = CreateStreamerEmbed(twitchStreamer)
            };

            await _restClient.ModifyMessageAsync(
                _settings.ChannelId,
                _activeBroadcasts.SummaryMessageId,
                message => message
                    .WithContent(string.Empty)
                    .AddEmbeds(newUserMessage.UserEmbed),
                cancellationToken: cancellationToken);

            _activeBroadcasts.Messages.Add(newUserMessage);

            _activeBroadcasts.SummaryMessageId = (await _restClient.SendMessageAsync(
                _settings.ChannelId,
                new MessageProperties().WithContent(CreateSummaryContent()),
                cancellationToken: cancellationToken)).Id;
        }
        catch (OperationCanceledException) { throw; /* TODO: Handle cancellation */ }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "{LogMessage} ({Data})", _logMessages.Errors.Default, twitchStreamer.UserData.Id);
        }
        finally { _semaphore.Release(); }
    }

    public async Task ProcessOfflineStateAsync(TwitchStreamer twitchStreamer, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);

        var userMessage = _activeBroadcasts.Messages
            .FirstOrDefault(
                message => message.UserData.Id == twitchStreamer.UserData.Id);

        // Skip the process if user not found (IsLive should already be marked as false)
        // TODO: Revisit the logic leading to this issue
        if (userMessage == null)
        {
            _logger.LogError(
                "Error with user {DisplayName} ({Id})", 
                twitchStreamer.UserData.DisplayName, twitchStreamer.UserData.Id);
            return;
        }

        if (userMessage.UserEmbed == null)
            throw new InvalidOperationException("UserEmbed can not be null"); // TODO: LogMessage

        try
        {
            var lastPosition = _activeBroadcasts.Messages.Max(message => message.Position);
            userMessage.UserEmbed = UpdateEmbed(userMessage.UserEmbed, userMessage);

            if (userMessage.Position == lastPosition)
            {
                await _restClient.ModifyMessageAsync(
                    _settings.ChannelId, userMessage.Id,
                    message => message.AddEmbeds(userMessage.UserEmbed),
                    cancellationToken: cancellationToken);

                _activeBroadcasts.Messages.Remove(userMessage);
            }
            else
            {
                // Find the oldest embed and swap places with
                // the offline user to readjust positions
                var oldestMessage = _activeBroadcasts.Messages
                    .First(message => message.Position == lastPosition);

                // Swap current embed UserData/UserEmbed/StreamData with the last embed
                (oldestMessage.UserData, userMessage.UserData) = (userMessage.UserData, oldestMessage.UserData);
                (oldestMessage.StreamData, userMessage.StreamData) = (userMessage.StreamData, oldestMessage.StreamData);
                (oldestMessage.UserEmbed, userMessage.UserEmbed) = (userMessage.UserEmbed, oldestMessage.UserEmbed);

                // Update the embed of the online user
                await _restClient.ModifyMessageAsync(
                    _settings.ChannelId, userMessage.Id,
                    message => message.AddEmbeds(userMessage.UserEmbed),
                    cancellationToken: cancellationToken);

                // Update the embed of the offline user
                await _restClient.ModifyMessageAsync(
                    _settings.ChannelId, oldestMessage.Id,
                    message => message.AddEmbeds(oldestMessage.UserEmbed),
                    cancellationToken: cancellationToken);

                // Removed the embed from the active broadcasts list
                _activeBroadcasts.Messages.Remove(oldestMessage);
            }
        }
        catch (OperationCanceledException) { throw; /* TODO: Handle cancellation */ }
        catch (RestException restException) when (restException.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Message for the user does not exist (Skipping)"); // TODO: LogMessage

            // Cleanup the entry of the user
            if (!_activeBroadcasts.Messages.Remove(userMessage))
                throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Offline Failure"); // TODO: LogMessage
            throw;
        }
        finally { _semaphore.Release(); }
    }

    public async Task ProcessUnchangedStateAsync(
        TwitchStreamer twitchStreamer,
        CancellationToken cancellationToken,
        bool refreshMedia = false)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var userMessage = _activeBroadcasts.Messages
                .FirstOrDefault(message => message.UserData.Id == twitchStreamer.UserData.Id)
                ?? throw new InvalidOperationException(_logMessages.Errors.UserWasNotFound);

            // TODO: Add an adjustment process to log and recover (re-create) the embed
            if (userMessage.UserEmbed is null || twitchStreamer.UserData is null || twitchStreamer.StreamData is null)
                throw new InvalidOperationException(_logMessages.Errors.Default); // TODO

            userMessage.UserData = twitchStreamer.UserData;
            userMessage.StreamData = twitchStreamer.StreamData;
            userMessage.UserEmbed = UpdateEmbed(userMessage.UserEmbed, userMessage, twitchStreamer.IsLive);

            // Only set the media if it has changed
            if (refreshMedia)
                RefreshMedia(userMessage.UserEmbed, userMessage);

            await _restClient.ModifyMessageAsync(
                _settings.ChannelId,
                userMessage.Id,
                message => message.AddEmbeds(userMessage.UserEmbed),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) { throw; /* TODO: Handle cancellation */ }
        catch (Exception exception)
        {
            _logger.LogError(exception, "RefreshMediaAsync failed to process"); // TODO: LogMessage
            throw;
        }
        finally { _semaphore.Release(); }
    }

    public async Task UpdateSummaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var messageProperties = new MessageProperties()
                .WithContent(CreateSummaryContent());

            var summaryMessage = _activeBroadcasts.SummaryMessageId != 0
                ? await _restClient.ModifyMessageAsync(
                    _settings.ChannelId,
                    _activeBroadcasts.SummaryMessageId,
                    message => message.WithContent(CreateSummaryContent()),
                    cancellationToken: cancellationToken)
                : await _restClient.SendMessageAsync(
                    _settings.ChannelId,
                    messageProperties,
                    cancellationToken: cancellationToken);

            _activeBroadcasts.SummaryMessageId = summaryMessage.Id;
            _logger.LogDebug("{LogMessage}", _logMessages.SummaryWasUpdated);
        }
        catch (OperationCanceledException) { throw; /* TODO: Handle cancellation */ }
        catch (RestException restException) when (restException.StatusCode == HttpStatusCode.NotFound)
        {
            // Specific handling for 404 Not Found
            _logger.LogWarning("Existing summary message was not found"); // TODO: LogMessage
            
            var newMessage = await _restClient.SendMessageAsync(
                _settings.ChannelId,
                new MessageProperties().WithContent(CreateSummaryContent()),
                cancellationToken: cancellationToken);

            _activeBroadcasts.SummaryMessageId = newMessage.Id;
            _logger.LogDebug("{LogMessage}", _logMessages.SummaryWasCreated);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception, 
                "{LogMessage}", 
                _logMessages.Errors.SummaryWasNotUpdated);

            // TODO: Search messages for last message that matches the summary format
            if (_activeBroadcasts.SummaryMessageId == 0)
            {
                var fallbackMessage = await _restClient.SendMessageAsync(
                    _settings.ChannelId,
                    new MessageProperties().WithContent(CreateSummaryContent()),
                    cancellationToken: cancellationToken);

                _activeBroadcasts.SummaryMessageId = fallbackMessage.Id;

                if (_activeBroadcasts.SummaryMessageId != 0)
                    _logger.LogDebug("{LogMessage}", _logMessages.SummaryWasUpdated);
                else throw;
            }
        }
        finally { await SaveDataAsync(cancellationToken); }
    }

    // TODO: Expand this further once other media features are added
    public static void RefreshMedia(
        EmbedProperties embed, 
        Broadcasts.Message message)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var url = $"https://www.twitch.tv/{message.StreamData.UserName}?{timestamp}";
        embed.WithUrl(url);
    }

    public bool HasEmbed(TwitchStreamer twitchStreamer) => 
        _activeBroadcasts.Messages
            .FirstOrDefault(message => message.UserData.Id == twitchStreamer.UserData.Id)?
            .UserEmbed is not null;

    private EmbedProperties ApplyBaseEmbedTemplate(EmbedProperties? embed = null)
    {
        embed ??= new EmbedProperties();
        
        embed.Timestamp = DateTimeOffset.UtcNow;
        embed.Author ??= new EmbedAuthorProperties();
        embed.Footer ??= new EmbedFooterProperties
        {
            Text = $"{_applicationName} v{_applicationVersion}",
            IconUrl = _settings.EmbedFooterIcon ?? string.Empty
        };
        embed.Fields ??= [];

        return embed;
    }

    private EmbedProperties CreateStreamerEmbed(TwitchStreamer twitchStreamer)
    {
        if (twitchStreamer.StreamData is null)
            throw new ArgumentNullException(nameof(twitchStreamer));

        return ApplyBaseEmbedTemplate()
            .WithTitle($"{twitchStreamer.UserData.DisplayName} is now live!") // TODO: Configurable
            .WithDescription(twitchStreamer.StreamData.Title)
            .WithImage(GetStreamPreviewUrl(twitchStreamer.UserData.Login))
            .WithUrl($"https://www.twitch.tv/{twitchStreamer.UserData.Login}")
            .WithColor(new NetCord.Color(_settings.EmbedColor.Online))
            .WithThumbnail(twitchStreamer.UserData.ProfileImageUrl)
            .AddFields(CreateOnlineFields(twitchStreamer.StreamData));
    }

    private EmbedProperties UpdateEmbed(
        EmbedProperties embed, 
        Broadcasts.Message message, 
        bool isLive = false)
    {
        if (!isLive)
        {
            var duration = FormatDuration(DateTime.UtcNow - message.StreamData.StartedAt);
            return ApplyBaseEmbedTemplate(embed)
                .WithTitle($"{message.UserData.DisplayName} finished streaming.") // TODO: Configurable
                .WithDescription(string.Empty)
                .WithImage(string.Empty)
                .WithColor(new NetCord.Color(_settings.EmbedColor.Offline))
                .WithFields(
                [
                    new EmbedFieldProperties()
                        .WithName("Stream Duration") // TODO: Configurable
                        .WithValue(duration)
                        .WithInline()
                ]);
        }

        return ApplyBaseEmbedTemplate(embed)
            .WithDescription(message.StreamData.Title)
            .WithImage(GetStreamPreviewUrl(message.UserData.Login))
            .WithThumbnail(message.UserData.ProfileImageUrl)
            .WithFields(CreateOnlineFields(message.StreamData));
    }

    // TODO: Configurable
    private string CreateSummaryContent() =>
        _activeBroadcasts.Messages is null or { Count: 0 }
            ? "## Active Streams\nNo streams are currently active"
            : $"""
              ## Active Streams
              {string.Join("\n", _activeBroadcasts.Messages
                  .OrderBy(user => user.Position)
                  .Select(CreateUserLink))}
              """;

    private string CreateUserLink(Broadcasts.Message user) =>
        $"- [{user.UserData.DisplayName}]" + 
        $"(https://discord.com/channels/{_settings.GuildId}/{_settings.ChannelId}/{user.Id})";

    /// <summary>
    /// Formats a TimeSpan duration into a human-readable string.
    /// </summary>
    /// <param name="duration">The duration to format.</param>
    /// <returns>Formatted duration string.</returns>    
    public static string FormatDuration(TimeSpan duration)
    {
        static string GetLabel(int value, string singular) => 
            $"{value} {singular}{(value == 1 ? "" : "s")}";

        if (duration.TotalMinutes < 60)
            return GetLabel((int)duration.TotalMinutes, "minute");

        int hours = (int)duration.TotalHours;
        int minutes = duration.Minutes;
        
        string hoursText = GetLabel(hours, "hour");
        return minutes > 0 
            ? $"{hoursText} and {GetLabel(minutes, "minute")}"
            : hoursText;
    }

    /// <summary>
    /// Generates a fileStream preview image url.
    /// </summary>
    /// <param name="userLogin">The login name of the Twitch Streamer.</param>
    /// <param name="width">The preview image width.</param>
    /// <param name="height">The preview image height.</param>
    /// <returns>A fileStream preview image url.</returns>    
    public static string GetStreamPreviewUrl(
        string userLogin, 
        int width = 1280, 
        int height = 720) => 
            $"https://static-cdn.jtvnw.net/previews-ttv/"
            + $"live_user_{userLogin}"
            + $"-{width}x{height}"
            + $".jpg?"
            + $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

    // TODO: Convert to both online and offline states usage
    private static List<EmbedFieldProperties> CreateOnlineFields(TwitchStream BroadcastData) =>
    [
        new EmbedFieldProperties()
            .WithName("Started") // TODO: Configurable
            .WithValue($"<t:{new DateTimeOffset(BroadcastData.StartedAt).ToUnixTimeSeconds()}:R>")
            .WithInline(),
        new EmbedFieldProperties()
            .WithName("Viewers") // TODO: Configurable
            .WithValue(BroadcastData.ViewerCount.ToString())
            .WithInline()
    ];
}

public sealed class Broadcasts
{
    public ulong SummaryMessageId { get; set; }
    public EmbedProperties? SummaryEmbed { get; set; }
    public List<Message> Messages { get; set; } = [];

    public sealed class Message
    {
        public required ulong Id { get; set; }
        public required int Position { get; set; }
        public required TwitchUser UserData { get; set; }
        public required TwitchStream StreamData { get; set; }
        public EmbedProperties? UserEmbed { get; set; }
    }
}
