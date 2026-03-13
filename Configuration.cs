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
    /// User-facing command and error strings.
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
/// Localized command and error strings.
/// </summary>
public sealed record LogMessages
{
    /// <summary>
    /// Localized error strings used in command responses.
    /// </summary>
    public GeneralError Errors { get; init; } = new();

    /// <summary>
    /// Message shown when a user is added to tracking.
    /// </summary>
    public string UserWasAdded { get; init; } = "User data was added";

    /// <summary>
    /// Message shown when a user is removed from tracking.
    /// </summary>
    public string UserWasRemoved { get; init; } = "User data was removed";

    /// <summary>
    /// Localized error strings.
    /// </summary>
    public sealed record GeneralError
    {
        /// <summary>
        /// Fallback error message for unexpected failures.
        /// </summary>
        public string Default { get; init; } = "An error has occurred";

        /// <summary>
        /// Error message shown when a Twitch user cannot be found.
        /// </summary>
        public string UserWasNotFound { get; init; } = "User was not found";

        /// <summary>
        /// Error message shown when a tracked user already exists.
        /// </summary>
        public string UserExists { get; init; } = "User exists";
    }
}
