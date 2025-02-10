using Microsoft.Extensions.Options;

using NetCord;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;

using TvGuide.Attributes;
using TvGuide.Modules;

namespace TvGuide.Commands;

[DynamicSlashCommand("CommandAddLiveUser")]
public class TvGuideCommandsModule(
    INowStreamingService nowStreamingService,
    IOptions<Configuration> settings)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly INowStreamingService _nowStreamingService = nowStreamingService;
    private readonly LogMessages _logMessages = settings.Value.LogMessages;
    private readonly Settings.NowLiveCommands _settings = settings.Value.NowLive.NowLiveCommands;

    private InteractionMessageProperties CreateCommandMessage(string content) => new()
    {
        Content = content,
        Flags = _settings.MessageFlag
    };

    [DynamicSubSlashCommand("SubCommandAddLiveUser")]
    public async Task<InteractionMessageProperties> AddStreamerCommand(
        [DynamicSlashCommandParameter("CommandParametersAddLiveUser")] string username) =>
            await _nowStreamingService.AddUserAsync(username, CancellationToken.None) switch
            {
                UserManagementResult.NotFound => CreateCommandMessage(_logMessages.Errors.UserWasNotFound),
                UserManagementResult.Success => CreateCommandMessage(_logMessages.UserWasAdded),
                UserManagementResult.AlreadyExists => CreateCommandMessage(_logMessages.Errors.UserExists),
                _ => CreateCommandMessage(_logMessages.Errors.Default)
            };

    [RequireUserPermissions<ApplicationCommandContext>(Permissions.ManageMessages)]
    [DynamicSubSlashCommand("SubCommandRemoveLiveUser")]
    public async Task<InteractionMessageProperties> RemoveStreamerCommand(
        [DynamicSlashCommandParameter("CommandParametersRemoveLiveUser")] string username) =>
            await _nowStreamingService.RemoveUserAsync(username, CancellationToken.None) switch
            {
                UserManagementResult.NotFound => CreateCommandMessage($"{_logMessages.Errors.UserWasNotFound} ({username})"),
                UserManagementResult.Success => CreateCommandMessage($"{_logMessages.UserWasRemoved} ({username})"),
                _ => CreateCommandMessage(_logMessages.Errors.Default)
            };
}
