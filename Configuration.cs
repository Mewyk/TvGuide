using NetCord;

namespace TvGuide;

public sealed record Configuration
{
    public required Settings.Discord Discord { get; init; }
    public required Settings.Twitch Twitch { get; init; }
    public required Settings.NowLive NowLive { get; init; }
    public required LogMessages LogMessages { get; init; } = new();
}

public static class Settings
{
    public sealed record Discord
    {
        public required string Token { get; init; }
        public required string[] Intents { get; init; }
    }

    public sealed record Twitch
    {
        public required TwitchAuthentication Authentication { get; init; }
        public required TwitchToken Token { get; init; }
    }

    public sealed record TwitchAuthentication
    {
        public required string ClientId { get; init; }
        public required string ClientSecret { get; init; }
    }

    public sealed record TwitchToken
    {
        public int TokenExpirationBuffer { get; init; } = 300;
        public int TokenRefreshInterval { get; init; } = 1800;
        public int TokenRetryDelay { get; init; } = 60;
        public int TokenMaxRetries { get; init; } = 3;
        public TimeSpan TokenMaxDelay { get; init; } = TimeSpan.FromMinutes(5);
        public TimeSpan TokenNormalDelay { get; init; } = TimeSpan.FromMinutes(10);
    }

    public sealed record NowLive
    {
        public required ulong GuildId { get; init; }
        public required ulong ChannelId { get; init; }
        public string UserDataFile { get; init; } = "NowLiveUserData.json";
        public string ActiveBroadcastsFile { get; init; } = "ActiveBroadcasts.json";
        public int UpdateInterval { get; init; } = 60;
        public int MediaRefreshInterval { get; init; } = 6;
        public int MaxUsersPerRequest { get; init; } = 100;
        public required string FooterIcon { get; init; }
        public NowLiveCommands NowLiveCommands { get; init; } = new();
        public required StatusColor StatusColor { get; init; }
    }

    public sealed record StatusColor
    {
        public required int Online { get; init; }
        public required int Offline { get; init; }
    }

    public sealed record NowLiveCommands
    {
        public MessageFlags MessageFlag { get; init; } = MessageFlags.Ephemeral;
    }
}

/// <summary>
/// Localized log messages and error strings.
/// </summary>
public sealed record LogMessages
{
    public Error Errors { get; init; } = new();

    public string DataWasLoaded { get; init; } = "Loaded the data";
    public string DataWasSaved { get; init; } = "Saved the data";
    public string UserWasAdded { get; init; } = "User data was added";
    public string UserWasRemoved { get; init; } = "User data was removed";
    public string UserWasLoaded { get; init; } = "User data was loaded";
    public string UserWasSaved { get; init; } = "User data was saved";
    public string StreamIsOnline { get; init; } = "Stream is now online";
    public string StreamIsOffline { get; init; } = "Stream is now offline";
    public string StreamIsUnchanged { get; init; } = "Stream has not changed from online";
    public string StreamMediaWasRefreshed { get; init; } = "Media refresh was processed";
    public string TokenWasRefreshed { get; init; } = "Twitch token was refreshed";

    public sealed record Error
    {
        public string Default { get; init; } = "An error has occurred";
        public string FileNotFound { get; init; } = "File was not found";
        public string UserWasNotFound { get; init; } = "User was not found";
        public string UserWasNotAdded { get; init; } = "User was not added";
        public string UserWasNotRemoved { get; init; } = "User was not removed";
        public string UserWasNotLoaded { get; init; } = "User was not loaded";
        public string UserWasNotSaved { get; init; } = "User was not saved";
        public string UserExists { get; init; } = "User exists";
        public string InputInvalid { get; init; } = "The supplied input is not valid";
        public string DataWasNotSaved { get; init; } = "Error saving data";
        public string DataWasNotLoaded { get; init; } = "Error loading data";
        public string MediaWasNotRefreshed { get; init; } = "Media refresh failed to process";
        public string TokenWasNotRefreshed { get; init; } = "Twitch token was not refreshed";
        public string WasNotUpdated { get; init; } = "Was not updated";
    }
}
