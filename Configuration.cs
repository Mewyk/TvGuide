using Microsoft.Extensions.Options;
using NetCord;

namespace TvGuide;

/// <summary>
/// Root application configuration.
/// </summary>
public sealed class Configuration
{
    /// <summary>
    /// Discord client settings.
    /// </summary>
    public Settings.Discord Discord { get; set; } = new();

    /// <summary>
    /// Now Live monitoring and presentation settings.
    /// </summary>
    public Settings.NowLive NowLive { get; set; } = new();

    /// <summary>
    /// User-facing command and error strings.
    /// </summary>
    public LogMessages LogMessages { get; set; } = new();
}

/// <summary>
/// Strongly typed configuration sections used throughout the application.
/// </summary>
public static class Settings
{
    /// <summary>
    /// Discord connection settings.
    /// </summary>
    public sealed class Discord
    {
        /// <summary>
        /// Discord bot token.
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Gateway intents enabled for the Discord client.
        /// </summary>
        public string[] Intents { get; set; } = [];
    }

    /// <summary>
    /// Settings for monitoring and publishing live Twitch broadcasts.
    /// </summary>
    public sealed class NowLive
    {
        /// <summary>
        /// Discord guild identifier that hosts the broadcast channel.
        /// </summary>
        public ulong GuildId { get; set; }

        /// <summary>
        /// Discord channel identifier where broadcast updates are posted.
        /// </summary>
        public ulong ChannelId { get; set; }

        /// <summary>
        /// File path used to persist tracked-user data.
        /// </summary>
        public string UserDataFile { get; set; } = "NowLiveUserData.json";

        /// <summary>
        /// File path used to persist active broadcast message state.
        /// </summary>
        public string ActiveBroadcastsFile { get; set; } = "ActiveBroadcasts.json";

        /// <summary>
        /// Interval, in seconds, between live-status checks.
        /// </summary>
        public int UpdateInterval { get; set; } = 60;

        /// <summary>
        /// Interval, in minutes, between preview-image refreshes for active broadcasts.
        /// </summary>
        public int MediaRefreshInterval { get; set; } = 6;

        /// <summary>
        /// Maximum number of Twitch users requested in a single batch.
        /// </summary>
        public int MaxUsersPerRequest { get; set; } = 100;

        /// <summary>
        /// Footer icon URL used in broadcast messaging.
        /// </summary>
        public string FooterIcon { get; set; } = string.Empty;

        /// <summary>
        /// Slash-command response settings for Now Live commands.
        /// </summary>
        public NowLiveCommands NowLiveCommands { get; set; } = new();

        /// <summary>
        /// Accent colors used for online and offline broadcast states.
        /// </summary>
        public StatusColor StatusColor { get; set; } = new();
    }

    /// <summary>
    /// Accent colors used for broadcast status displays.
    /// </summary>
    public sealed class StatusColor
    {
        /// <summary>
        /// Color applied to active or online broadcasts.
        /// </summary>
        public int Online { get; set; }

        /// <summary>
        /// Color applied to offline or finished broadcasts.
        /// </summary>
        public int Offline { get; set; }
    }

    /// <summary>
    /// Settings for Now Live slash-command responses.
    /// </summary>
    public sealed class NowLiveCommands
    {
        /// <summary>
        /// Flags applied to interaction responses returned by command handlers.
        /// </summary>
        public MessageFlags MessageFlag { get; set; } = MessageFlags.Ephemeral;
    }
}

/// <summary>
/// Localized command and error strings.
/// </summary>
public sealed class LogMessages
{
    /// <summary>
    /// Localized error strings used in command responses.
    /// </summary>
    public GeneralError Errors { get; set; } = new();

    /// <summary>
    /// Message shown when a user is added to tracking.
    /// </summary>
    public string UserWasAdded { get; set; } = "User data was added";

    /// <summary>
    /// Message shown when a user is removed from tracking.
    /// </summary>
    public string UserWasRemoved { get; set; } = "User data was removed";

    /// <summary>
    /// Localized error strings.
    /// </summary>
    public sealed class GeneralError
    {
        /// <summary>
        /// Fallback error message for unexpected failures.
        /// </summary>
        public string Default { get; set; } = "An error has occurred";

        /// <summary>
        /// Error message shown when a Twitch user cannot be found.
        /// </summary>
        public string UserWasNotFound { get; set; } = "User was not found";

        /// <summary>
        /// Error message shown when a tracked user already exists.
        /// </summary>
        public string UserExists { get; set; } = "User exists";
    }
}

/// <summary>
/// Validates application configuration that is owned by TvGuide.
/// </summary>
public sealed class ConfigurationValidator : IValidateOptions<Configuration>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, Configuration options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (options.NowLive.GuildId == 0)
            failures.Add("NowLive:GuildId must be greater than 0.");

        if (options.NowLive.ChannelId == 0)
            failures.Add("NowLive:ChannelId must be greater than 0.");

        if (string.IsNullOrWhiteSpace(options.NowLive.UserDataFile))
            failures.Add("NowLive:UserDataFile is required.");

        if (string.IsNullOrWhiteSpace(options.NowLive.ActiveBroadcastsFile))
            failures.Add("NowLive:ActiveBroadcastsFile is required.");

        if (options.NowLive.UpdateInterval <= 0)
            failures.Add("NowLive:UpdateInterval must be greater than 0.");

        if (options.NowLive.MediaRefreshInterval <= 0)
            failures.Add("NowLive:MediaRefreshInterval must be greater than 0.");

        if (options.NowLive.MaxUsersPerRequest is < 1 or > 100)
            failures.Add("NowLive:MaxUsersPerRequest must be between 1 and 100.");

        if (string.IsNullOrWhiteSpace(options.NowLive.FooterIcon))
        {
            failures.Add("NowLive:FooterIcon is required.");
        }
        else if (!Uri.TryCreate(options.NowLive.FooterIcon, UriKind.Absolute, out _))
        {
            failures.Add("NowLive:FooterIcon must be an absolute URI.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
