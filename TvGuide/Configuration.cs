using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using NetCord;

namespace TvGuide;

public class Configuration
{
    public required Settings.Discord Discord { get; set; }
    public required Settings.Twitch Twitch { get; set; }
    public required Settings.NowLive NowLive { get; set; }
    public required LogMessages LogMessages { get; set; } = new LogMessages();
    public required string ApplicationName { get; set; } = GetApplicationName;
    public required string ApplicationVersion { get; set; } = GetApplicationVersion;

    public static string GetApplicationVersion => Assembly
        .GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+')[0] ?? "0.0.0";

    public static string GetApplicationName => Assembly
        .GetExecutingAssembly().GetName().Name ?? "TvGuide";
}

public class Settings
{
    public class Discord
    {
        public required string Token { get; set; }
        public required string[] Intents { get; set; }
    }

    public class Twitch
    {
        public required TwitchAuthentication Authentication { get; set; }
        public required TwitchToken Token { get; set; }
    }

    public record TwitchAuthentication
    {
        public required string ClientId { get; set; }
        public required string ClientSecret { get; set; }

    }

    public class TwitchToken
    {
        public int TokenExpirationBuffer { get; set; } = 300;
        public int TokenRefreshInterval { get; set; } = 1800;
        public int TokenRetryDelay { get; set; } = 60;
        public int TokenMaxRetries { get; set; } = 3;
        public TimeSpan TokenMaxDelay { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan TokenNormalDelay { get; set; } = TimeSpan.FromMinutes(10);
    }

    public class NowLive
    {
        public ulong GuildId { get; set; }
        public ulong ChannelId { get; set; }

        public string UserDataFile { get; init; } = "NowLiveUserData.json";
        public string ActiveBroadcastsFile { get; init; } = "ActiveBroadcasts.json";

        public int UpdateInterval { get; set; } = 60;
        public int MediaRefreshInterval { get; set; } = 6;
        public int MaxUsersPerRequest { get; set; } = 100;
        public required string FooterIcon { get; set; }

        public NowLiveCommands NowLiveCommands { get; set; } = new();
        public required StatusColor StatusColor { get; set; }
    }

    public class StatusColor
    {
        public required int Online { get; set; }
        public required int Offline { get; set; }
    }

    public class NowLiveCommands
    {
        [JsonConverter(typeof(MessageFlagsConverter))]
        public MessageFlags MessageFlag { get; set; } = MessageFlags.Ephemeral;
    }
}

public class LogMessages
{
    public Error Errors { get; set; } = new();

    // General
    public string DataWasLoaded { get; set; } = "Loaded the data";
    public string DataWasSaved { get; set; } = "Saved the data";

    public string UserWasAdded { get; set; } = "User data was added";
    public string UserWasRemoved { get; set; } = "User data was removed";
    public string UserWasLoaded { get; set; } = "User data was loaded";
    public string UserWasSaved { get; set; } = "User data was saved";

    public string StreamIsOnline { get; set; } = "Stream is now online";
    public string StreamIsOffline { get; set; } = "Stream is now offline";
    public string StreamIsUnchanged { get; set; } = "Stream has not changed from online";
    public string StreamMediaWasRefreshed { get; set; } = "Media refresh was processed";

    public string SummaryWasCreated { get; set; } = "Summary message was created";
    public string SummaryWasUpdated { get; set; } = "Summary message was updated";

    public string TokenWasRefreshed { get; set; } = "Twitch token was refreshed";

    

    public class Error
    {
        public string Default { get; set; } = "An error has occurred";

        public string FileNotFound { get; set; } = "File was not found";

        public string UserWasNotFound { get; set; } = "User was not found";
        public string UserWasNotAdded { get; set; } = "User was not added";
        public string UserWasNotRemoved { get; set; } = "User was not removed";
        public string UserWasNotLoaded { get; set; } = "User was not loaded";
        public string UserWasNotSaved { get; set; } = "User was not saved";
        public string UserExists { get; set; } = "User exists";

        public string InputInvalid { get; set; } = "The supplied input is not valid";

        public string DataWasNotSaved { get; set; } = "Error loading data";
        public string DataWasNotLoaded { get; set; } = "Error saving data";

        public string MediaWasNotRefreshed { get; set; } = "Media refresh failed to process";

        public string SummaryWasNotCreated { get; set; } = "Summary message was not created";
        public string SummaryWasNotUpdated { get; set; } = "Summary message was not updated";
        public string SummaryWasNotSet { get; set; } = "Summary message identifier was not set";

        public string JsonWasNotProcessed { get; set; } = "Failed to process json data";

        public string TokenWasNotRefreshed { get; set; } = "Twitch token was not refreshed";
        
        public string WasNotUpdated { get; set; } = "Was not updated";
    }
}

public class MessageFlagsConverter : JsonConverter<MessageFlags>
{
    public override MessageFlags Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) 
            => (MessageFlags)reader.GetUInt32();

    public override void Write(
        Utf8JsonWriter writer, MessageFlags value, JsonSerializerOptions options)
            => writer.WriteNumberValue((uint)value);
}
