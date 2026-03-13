using NetCord;

namespace TvGuide;

/// <summary>
/// Root application configuration.
/// </summary>
public sealed record Configuration
{
    /// <summary>
    /// Discord client settings.
    /// </summary>
    public required Settings.Discord Discord { get; init; }

    /// <summary>
    /// Twitch API settings.
    /// </summary>
    public required Settings.Twitch Twitch { get; init; }

    /// <summary>
    /// Now Live monitoring and presentation settings.
    /// </summary>
    public required Settings.NowLive NowLive { get; init; }

    /// <summary>
    /// Localized log and error messages.
    /// </summary>
    public required LogMessages LogMessages { get; init; } = new();
}

/// <summary>
/// Strongly typed configuration sections used throughout the application.
/// </summary>
public static class Settings
{
    /// <summary>
    /// Discord connection settings.
    /// </summary>
    public sealed record Discord
    {
        /// <summary>
        /// Discord bot token.
        /// </summary>
        public required string Token { get; init; }

        /// <summary>
        /// Gateway intents enabled for the Discord client.
        /// </summary>
        public required string[] Intents { get; init; }
    }

    /// <summary>
    /// Twitch integration settings.
    /// </summary>
    public sealed record Twitch
    {
        /// <summary>
        /// OAuth client credentials for Twitch.
        /// </summary>
        public required TwitchAuthentication Authentication { get; init; }

        /// <summary>
        /// Twitch access-token refresh settings.
        /// </summary>
        public required TwitchToken Token { get; init; }
    }

    /// <summary>
    /// Twitch OAuth client credentials.
    /// </summary>
    public sealed record TwitchAuthentication
    {
        /// <summary>
        /// Twitch application client identifier.
        /// </summary>
        public required string ClientId { get; init; }

        /// <summary>
        /// Twitch application client secret.
        /// </summary>
        public required string ClientSecret { get; init; }
    }

    /// <summary>
    /// Settings that control Twitch access-token refresh behavior.
    /// </summary>
    public sealed record TwitchToken
    {
        /// <summary>
        /// Number of seconds subtracted from token expiry when deciding whether a token is still usable.
        /// </summary>
        public int TokenExpirationBuffer { get; init; } = 300;

        /// <summary>
        /// Configured refresh interval, in seconds, for refreshing Twitch access tokens.
        /// </summary>
        public int TokenRefreshInterval { get; init; } = 1800;

        /// <summary>
        /// Configured retry delay, in seconds, for token refresh failures.
        /// </summary>
        public int TokenRetryDelay { get; init; } = 60;

        /// <summary>
        /// Maximum retry count considered when calculating token-refresh backoff.
        /// </summary>
        public int TokenMaxRetries { get; init; } = 3;

        /// <summary>
        /// Maximum delay between token refresh retries.
        /// </summary>
        public TimeSpan TokenMaxDelay { get; init; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Delay between successful token refresh checks.
        /// </summary>
        public TimeSpan TokenNormalDelay { get; init; } = TimeSpan.FromMinutes(10);
    }

    /// <summary>
    /// Settings for monitoring and publishing live Twitch broadcasts.
    /// </summary>
    public sealed record NowLive
    {
        /// <summary>
        /// Discord guild identifier that hosts the broadcast channel.
        /// </summary>
        public required ulong GuildId { get; init; }

        /// <summary>
        /// Discord channel identifier where broadcast updates are posted.
        /// </summary>
        public required ulong ChannelId { get; init; }

        /// <summary>
        /// File path used to persist tracked-user data.
        /// </summary>
        public string UserDataFile { get; init; } = "NowLiveUserData.json";

        /// <summary>
        /// File path used to persist active broadcast message state.
        /// </summary>
        public string ActiveBroadcastsFile { get; init; } = "ActiveBroadcasts.json";

        /// <summary>
        /// Interval, in seconds, between live-status checks.
        /// </summary>
        public int UpdateInterval { get; init; } = 60;

        /// <summary>
        /// Interval, in minutes, between preview-image refreshes for active broadcasts.
        /// </summary>
        public int MediaRefreshInterval { get; init; } = 6;

        /// <summary>
        /// Maximum number of Twitch users requested in a single batch.
        /// </summary>
        public int MaxUsersPerRequest { get; init; } = 100;

        /// <summary>
        /// Footer icon URL used in broadcast messaging.
        /// </summary>
        public required string FooterIcon { get; init; }

        /// <summary>
        /// Slash-command response settings for Now Live commands.
        /// </summary>
        public NowLiveCommands NowLiveCommands { get; init; } = new();

        /// <summary>
        /// Accent colors used for online and offline broadcast states.
        /// </summary>
        public required StatusColor StatusColor { get; init; }
    }

    /// <summary>
    /// Accent colors used for broadcast status displays.
    /// </summary>
    public sealed record StatusColor
    {
        /// <summary>
        /// Color applied to active or online broadcasts.
        /// </summary>
        public required int Online { get; init; }

        /// <summary>
        /// Color applied to offline or finished broadcasts.
        /// </summary>
        public required int Offline { get; init; }
    }

    /// <summary>
    /// Settings for Now Live slash-command responses.
    /// </summary>
    public sealed record NowLiveCommands
    {
        /// <summary>
        /// Flags applied to interaction responses returned by command handlers.
        /// </summary>
        public MessageFlags MessageFlag { get; init; } = MessageFlags.Ephemeral;
    }
}

/// <summary>
/// Localized log messages and error strings.
/// </summary>
public sealed record LogMessages
{
    /// <summary>
    /// Localized error messages.
    /// </summary>
    public Error Errors { get; init; } = new();

    /// <summary>
    /// Message logged when persisted data is loaded successfully.
    /// </summary>
    public string DataWasLoaded { get; init; } = "Loaded the data";

    /// <summary>
    /// Message logged when persisted data is saved successfully.
    /// </summary>
    public string DataWasSaved { get; init; } = "Saved the data";

    /// <summary>
    /// Message logged when a user is added to tracking.
    /// </summary>
    public string UserWasAdded { get; init; } = "User data was added";

    /// <summary>
    /// Message logged when a user is removed from tracking.
    /// </summary>
    public string UserWasRemoved { get; init; } = "User data was removed";

    /// <summary>
    /// Message logged when tracked-user data is loaded.
    /// </summary>
    public string UserWasLoaded { get; init; } = "User data was loaded";

    /// <summary>
    /// Message logged when tracked-user data is saved.
    /// </summary>
    public string UserWasSaved { get; init; } = "User data was saved";

    /// <summary>
    /// Message logged when a stream is detected as live.
    /// </summary>
    public string StreamIsOnline { get; init; } = "Stream is now online";

    /// <summary>
    /// Message logged when a stream goes offline.
    /// </summary>
    public string StreamIsOffline { get; init; } = "Stream is now offline";

    /// <summary>
    /// Message logged when a stream remains live with no state change.
    /// </summary>
    public string StreamIsUnchanged { get; init; } = "Stream has not changed from online";

    /// <summary>
    /// Message logged when broadcast media is refreshed.
    /// </summary>
    public string StreamMediaWasRefreshed { get; init; } = "Media refresh was processed";

    /// <summary>
    /// Message logged when a Twitch access token is refreshed.
    /// </summary>
    public string TokenWasRefreshed { get; init; } = "Twitch token was refreshed";

    /// <summary>
    /// Localized error-message values.
    /// </summary>
    public sealed record Error
    {
        /// <summary>
        /// Fallback error message for unexpected failures.
        /// </summary>
        public string Default { get; init; } = "An error has occurred";

        /// <summary>
        /// Error message used when a required file cannot be found.
        /// </summary>
        public string FileNotFound { get; init; } = "File was not found";

        /// <summary>
        /// Error message used when a Twitch user cannot be found.
        /// </summary>
        public string UserWasNotFound { get; init; } = "User was not found";

        /// <summary>
        /// Error message used when a user could not be added to tracking.
        /// </summary>
        public string UserWasNotAdded { get; init; } = "User was not added";

        /// <summary>
        /// Error message used when a user could not be removed from tracking.
        /// </summary>
        public string UserWasNotRemoved { get; init; } = "User was not removed";

        /// <summary>
        /// Error message used when tracked-user data could not be loaded.
        /// </summary>
        public string UserWasNotLoaded { get; init; } = "User was not loaded";

        /// <summary>
        /// Error message used when tracked-user data could not be saved.
        /// </summary>
        public string UserWasNotSaved { get; init; } = "User was not saved";

        /// <summary>
        /// Error message used when a tracked user already exists.
        /// </summary>
        public string UserExists { get; init; } = "User exists";

        /// <summary>
        /// Error message used when supplied input fails validation.
        /// </summary>
        public string InputInvalid { get; init; } = "The supplied input is not valid";

        /// <summary>
        /// Error message used when data persistence fails during save.
        /// </summary>
        public string DataWasNotSaved { get; init; } = "Error saving data";

        /// <summary>
        /// Error message used when data persistence fails during load.
        /// </summary>
        public string DataWasNotLoaded { get; init; } = "Error loading data";

        /// <summary>
        /// Error message used when media refresh processing fails.
        /// </summary>
        public string MediaWasNotRefreshed { get; init; } = "Media refresh failed to process";

        /// <summary>
        /// Error message used when Twitch token refresh fails.
        /// </summary>
        public string TokenWasNotRefreshed { get; init; } = "Twitch token was not refreshed";

        /// <summary>
        /// Error message used when a requested update operation fails.
        /// </summary>
        public string WasNotUpdated { get; init; } = "Was not updated";
    }
}
