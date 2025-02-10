using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NetCord.Rest;

namespace TvGuide.Modules;

// TODO: Break things down by classes for better organization and such
public class ActiveStreamsModule(
    IOptions<Configuration> settings,
    ILogger<ActiveStreamsModule> logger,
    RestClient restClient)
{
    private readonly RestClient _restClient = restClient;
    private readonly Settings.NowLive _settings = settings.Value.NowLive;
    private readonly LogMessages _logMessages = settings.Value.LogMessages;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private ActiveStream _activeStreams = new();
    private readonly string _applicationName = settings.Value.ApplicationName;
    private readonly string _applicationVersion = settings.Value.ApplicationVersion;
    private readonly ILogger<ActiveStreamsModule> _logger = logger;

    public async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_settings.ActiveStreamsFile))
            {
                _activeStreams = new ActiveStream();
                _logger.LogInformation("{LogMessage} ({Data})",
                    _logMessages.Errors.FileNotFound,
                    _settings.ActiveStreamsFile);
                return;
            }

            using FileStream fileStream = File.OpenRead(_settings.ActiveStreamsFile);
            if (fileStream.Length == 0)
            {
                _activeStreams = new ActiveStream();
                return;
            }

            _activeStreams = await JsonSerializer.DeserializeAsync<ActiveStream>(
                fileStream, cancellationToken: cancellationToken) ?? new ActiveStream();

            _activeStreams.Messages ??= [];

            _logger.LogInformation(
                "{LogMessage} ({Data})",
                _logMessages.DataWasLoaded,
                _settings.ActiveStreamsFile);
        }
        catch (OperationCanceledException) { throw; /* TODO: Handle cancellation */ }
        catch (Exception exception)
        {
            _logger.LogError(exception, 
                "{LogMessage} ({Data})",
                _logMessages.Errors.DataWasNotLoaded, 
                _settings.ActiveStreamsFile);
            _activeStreams = new ActiveStream();
        }
        finally { _semaphore.Release(); }
    }

    private async Task SaveDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            using FileStream fileStream = File.Create(_settings.ActiveStreamsFile);
            await JsonSerializer.SerializeAsync(
                fileStream,
                _activeStreams,
                cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "{LogMessage} ({Data})",
                _logMessages.Errors.JsonWasNotProcessed,
                _settings.ActiveStreamsFile);
            throw;
        }
        finally { /* TODO: Do something */ }
    }

    public async Task ProcessOnlineStateAsync(Streamer streamer, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            foreach (var existingMessage in _activeStreams.Messages)
                existingMessage.Position += 1;

            var newUserMessage = new ActiveStream.Message
            {
                Id = _activeStreams.SummaryMessageId,
                Position = 1,
                UserData = streamer.UserData,
                StreamData = streamer.StreamData
                    ?? throw new ArgumentNullException(nameof(streamer)),
                UserEmbed = CreateStreamerEmbed(streamer)
            };

            await _restClient.ModifyMessageAsync(
                _settings.ChannelId,
                _activeStreams.SummaryMessageId,
                message => message
                    .WithContent(string.Empty)
                    .AddEmbeds(newUserMessage.UserEmbed),
                cancellationToken: cancellationToken);

            _activeStreams.Messages.Add(newUserMessage);

            _activeStreams.SummaryMessageId = (await _restClient.SendMessageAsync(
                _settings.ChannelId,
                new MessageProperties().WithContent(CreateSummaryContent()),
                cancellationToken: cancellationToken)).Id;
        }
        catch (OperationCanceledException) { throw; /* TODO: Handle cancellation */ }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "{LogMessage} ({Data})", _logMessages.Errors.Default, streamer.UserData.Id);
        }
        finally { _semaphore.Release(); }
    }

    public async Task ProcessOfflineStateAsync(Streamer streamer, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);

        var userMessage = _activeStreams.Messages
            .FirstOrDefault(
                message => message.UserData.Id == streamer.UserData.Id);

        // Skip the process if user not found (IsLive should already be marked as false)
        // TODO: Revisit the logic leading to this issue
        if (userMessage == null)
        {
            _logger.LogError(
                "Error with user {DisplayName} ({Id})", 
                streamer.UserData.DisplayName, streamer.UserData.Id);
            return;
        }

        if (userMessage.UserEmbed == null)
            throw new InvalidOperationException("UserEmbed can not be null"); // TODO: LogMessage

        try
        {
            var lastPosition = _activeStreams.Messages.Max(message => message.Position);
            userMessage.UserEmbed = UpdateEmbed(userMessage.UserEmbed, userMessage);

            if (userMessage.Position == lastPosition)
            {
                await _restClient.ModifyMessageAsync(
                    _settings.ChannelId, userMessage.Id,
                    message => message.AddEmbeds(userMessage.UserEmbed),
                    cancellationToken: cancellationToken);

                _activeStreams.Messages.Remove(userMessage);
            }
            else
            {
                // Find the oldest embed and swap places with
                // the offline user to readjust positions
                var oldestMessage = _activeStreams.Messages
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

                // Removed the embed from the active streams list
                _activeStreams.Messages.Remove(oldestMessage);
            }
        }
        catch (OperationCanceledException) { throw; /* TODO: Handle cancellation */ }
        catch (RestException restException) when (restException.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Message for the user does not exist (Skipping)"); // TODO: LogMessage

            // Cleanup the entry of the user
            if (!_activeStreams.Messages.Remove(userMessage))
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
        Streamer streamer,
        CancellationToken cancellationToken,
        bool refreshMedia = false)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var userMessage = _activeStreams.Messages
                .FirstOrDefault(message => message.UserData.Id == streamer.UserData.Id)
                ?? throw new InvalidOperationException(_logMessages.Errors.UserWasNotFound);

            // TODO: Add an adjustment process to log and recover (re-create) the embed
            if (userMessage.UserEmbed == null)
                throw new InvalidOperationException(_logMessages.Errors.Default); // TODO

            if (streamer.UserData != null)
                userMessage.UserData = streamer.UserData;
            else
                throw new InvalidOperationException(_logMessages.Errors.Default); // TODO

            if (streamer.StreamData != null)
                userMessage.StreamData = streamer.StreamData;
            else
                throw new InvalidOperationException(_logMessages.Errors.Default); // TODO

            userMessage.UserEmbed = UpdateEmbed(userMessage.UserEmbed, userMessage, streamer.IsLive);

            // Only set the media if it has changed
            if (refreshMedia)
                RefreshMedia(userMessage.UserEmbed, userMessage);

            await _restClient.ModifyMessageAsync(
                _settings.ChannelId,
                userMessage.Id,
                message => message
                    .AddEmbeds(userMessage.UserEmbed),
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
            _activeStreams.SummaryMessageId = _activeStreams.SummaryMessageId != 0
                ? (await _restClient.ModifyMessageAsync(
                    _settings.ChannelId,
                    _activeStreams.SummaryMessageId,
                    message => message.WithContent(CreateSummaryContent()),
                    cancellationToken: cancellationToken)).Id
                : (await _restClient.SendMessageAsync(
                    _settings.ChannelId,
                    new MessageProperties().WithContent(CreateSummaryContent()),
                    cancellationToken: cancellationToken)).Id;

            _logger.LogDebug("{LogMessage}", _logMessages.SummaryWasUpdated);
        }
        catch (OperationCanceledException) { throw; /* TODO: Handle cancellation */ }
        catch (RestException restException) when (restException.StatusCode == HttpStatusCode.NotFound)
        {
            // Specific handling for 404 Not Found
            _logger.LogWarning("Existing summary message was not found"); // TODO: LogMessage
            _activeStreams.SummaryMessageId = (await _restClient.SendMessageAsync(
                _settings.ChannelId,
                new MessageProperties().WithContent(CreateSummaryContent()),
                cancellationToken: cancellationToken)).Id;

            _logger.LogDebug("{LogMessage}", _logMessages.SummaryWasCreated);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "{LogMessage}", _logMessages.Errors.SummaryWasNotUpdated);

            // TODO: Search messages for last message that matches the summary format
            if (_activeStreams.SummaryMessageId == 0)
            {
                _activeStreams.SummaryMessageId = (await _restClient.SendMessageAsync(
                    _settings.ChannelId,
                    new MessageProperties().WithContent(CreateSummaryContent()),
                    cancellationToken: cancellationToken)).Id;

                if (_activeStreams.SummaryMessageId != 0)
                    _logger.LogDebug("{LogMessage}", _logMessages.SummaryWasUpdated);
                else throw;
            }
        }
        finally { await SaveDataAsync(cancellationToken); }
    }

    // TODO: Expand this further once other media features are added
    public static void RefreshMedia(EmbedProperties embed, ActiveStream.Message message) => embed.WithUrl(
        $"https://www.twitch.tv/" +
        $"{message.StreamData.UserName}?" +
        $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");

    public bool HasEmbed(Streamer streamer) => _activeStreams.Messages
        .FirstOrDefault(message => message.UserData.Id == streamer.UserData.Id)?
        .UserEmbed != null;

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

    private EmbedProperties CreateStreamerEmbed(Streamer streamer) => streamer.StreamData == null
        ? throw new ArgumentNullException(nameof(streamer))
        : ApplyBaseEmbedTemplate()
            .WithTitle($"{streamer.UserData.DisplayName} is now live!") // TODO: Configurable
            .WithDescription(streamer.StreamData.Title)
            .WithImage(GetStreamPreviewUrl(streamer.UserData.Login))
            .WithUrl($"https://www.twitch.tv/{streamer.UserData.Login}")
            .WithColor(new NetCord.Color(_settings.EmbedColor.Online))
            .WithThumbnail(streamer.UserData.ProfileImageUrl)
            .AddFields(CreateOnlineFields(streamer.StreamData));

    private EmbedProperties UpdateEmbed(EmbedProperties embed, ActiveStream.Message message, bool isLive = false) => 
        !isLive
        ? ApplyBaseEmbedTemplate(embed)
            .WithTitle($"{message.UserData.DisplayName} finished streaming.") // TODO: Configurable
            .WithDescription(string.Empty)
            .WithImage(string.Empty)
            .WithColor(new NetCord.Color(_settings.EmbedColor.Offline))
            .WithFields(
            [
                new EmbedFieldProperties()
                    .WithName("Stream Duration") // TODO: Configurable
                    .WithValue(
                        FormatDuration(DateTime.UtcNow - message.StreamData.StartedAt))
                    .WithInline()
            ])
        : ApplyBaseEmbedTemplate(embed)
            .WithDescription(message.StreamData.Title)
            .WithImage(GetStreamPreviewUrl(message.UserData.Login))
            .WithThumbnail(message.UserData.ProfileImageUrl)
            .WithFields(CreateOnlineFields(message.StreamData));

    // TODO: Configurable
    private string CreateSummaryContent() => _activeStreams.Messages == null || _activeStreams.Messages.Count == 0
        ? "## Active Streams\nNo streams are currently active"
        : $"## Active Streams\n{string
            .Join("\n", _activeStreams.Messages
                .OrderBy(user => user.Position)
                .Select(user =>
                    $"- [{user.UserData.DisplayName}](https://discord.com/channels/{_settings.GuildId}/{_settings.ChannelId}/{user.Id})"))}";

    /// <summary>
    /// Formats a TimeSpan duration into a human-readable string.
    /// </summary>
    /// <param name="duration">The duration to format.</param>
    /// <returns>Formatted duration string.</returns>    
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes >= 60)
        {
            int hours = (int)duration.TotalHours;
            int minutes = duration.Minutes;
            string hourText = hours == 1 ? "hour" : "hours";
            string minuteText = minutes == 1 ? "minute" : "minutes";
            return minutes > 0 ? $"{hours} {hourText} and {minutes} {minuteText}" : $"{hours} {hourText}";
        }
        else
        {
            int minutes = (int)duration.TotalMinutes;
            string minuteText = minutes == 1 ? "minute" : "minutes";
            return $"{minutes} {minuteText}";
        }
    }

    /// <summary>
    /// Generates a fileStream preview image url.
    /// </summary>
    /// <param name="userLogin">The login name of the streamer.</param>
    /// <param name="width">The preview image width.</param>
    /// <param name="height">The preview image height.</param>
    /// <returns>A fileStream preview image url.</returns>    
    public static string GetStreamPreviewUrl(string userLogin, int width = 1280, int height = 720)
    {
        var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"https://static-cdn.jtvnw.net/previews-ttv/live_user_{userLogin}-{width}x{height}.jpg?{unixTime}";
    }

    // TODO: Convert to both online and offline states usage
    private static List<EmbedFieldProperties> CreateOnlineFields(TwitchStream streamData)
    {
        return
        [
            new EmbedFieldProperties()
                .WithName("Started") // TODO: Configurable
                .WithValue($"<t:{new DateTimeOffset(streamData.StartedAt).ToUnixTimeSeconds()}:R>")
                .WithInline(),
            new EmbedFieldProperties()
                .WithName("Viewers") // TODO: Configurable
                .WithValue(streamData.ViewerCount.ToString())
                .WithInline()
        ];
    }
}

public class ActiveStream
{
    public ulong SummaryMessageId { get; set; }
    public EmbedProperties? SummaryEmbed { get; set; }
    public List<Message> Messages { get; set; } = [];

    public class Message
    {
        public required ulong Id { get; set; }
        public required int Position { get; set; }
        public required TwitchUser UserData { get; set; }
        public required TwitchStream StreamData { get; set; }
        public EmbedProperties? UserEmbed { get; set; }
    }
}
