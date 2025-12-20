using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using TvGuide.Attributes;
using TvGuide.Modules;

namespace TvGuide.Commands;

[DynamicSlashCommand("CommandAddLiveUser")]
public sealed class TvGuideCommandsModule(
    INowLiveService nowLiveService,
    IOptions<Configuration> settings
) : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly INowLiveService _nowLiveService = nowLiveService;
    private readonly LogMessages _logMessages = settings.Value.LogMessages;
    private readonly Settings.NowLiveCommands _settings = settings.Value.NowLive.NowLiveCommands;

    private InteractionMessageProperties CreateMessage(string content) => new()
    {
        Content = content,
        Flags = _settings.MessageFlag
    };

    [DynamicSubSlashCommand("SubCommandAddLiveUser")]
    public async Task<InteractionMessageProperties> AddStreamerCommand(
        [DynamicSlashCommandParameter("CommandParametersAddLiveUser")] string username)
        => await _nowLiveService
            .AddUserAsync(username, default)
            .ConfigureAwait(false) 
            switch
            {
                UserManagementResult.NotFound => CreateMessage($"{_logMessages.Errors.UserWasNotFound} ({username})"),
                UserManagementResult.Success => CreateMessage($"{_logMessages.UserWasAdded} ({username})"),
                UserManagementResult.AlreadyExists => CreateMessage($"{_logMessages.Errors.UserExists} ({username})"),
                _ => CreateMessage(_logMessages.Errors.Default)
            };

    [RequireUserPermissions<ApplicationCommandContext>(Permissions.ManageMessages)]
    [DynamicSubSlashCommand("SubCommandRemoveLiveUser")]
    public async Task<InteractionMessageProperties> RemoveStreamerCommand(
        [DynamicSlashCommandParameter("CommandParametersRemoveLiveUser")] string username)
        => await _nowLiveService
            .RemoveUserAsync(username, default)
            .ConfigureAwait(false)
            switch
            {
                UserManagementResult.NotFound => CreateMessage($"{_logMessages.Errors.UserWasNotFound} ({username})"),
                UserManagementResult.Success => CreateMessage($"{_logMessages.UserWasRemoved} ({username})"),
                _ => CreateMessage(_logMessages.Errors.Default)
            };
}
