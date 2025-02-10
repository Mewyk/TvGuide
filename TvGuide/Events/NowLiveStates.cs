using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using TvGuide.Modules;

namespace TvGuide.Events;

public class NowLiveStates(
    ILogger<NowLiveStates> logger,
    IOptions<Configuration> settings,
    ActiveStreamsModule activeStreamsModule)
{
    private readonly ILogger<NowLiveStates> _logger = logger;
    private readonly LogMessages _logMessages = settings.Value.LogMessages;
    private readonly ActiveStreamsModule _nowStreaming = activeStreamsModule;

    public async void OnStreamStateOnline(object? sender, UsersEventArguments eventData)
    {
        _logger.LogDebug("{LogMessage} (Total: {Data})", _logMessages.StreamIsOnline, eventData.Users.Count);
        var tasks = eventData.Users.Select(user =>
            _nowStreaming.ProcessOnlineStateAsync(user, eventData.CancellationToken));

        await Task.WhenAll(tasks);

        await _nowStreaming.UpdateSummaryAsync(eventData.CancellationToken);
    }

    public async void OnStreamStateOffline(object? sender, UsersEventArguments eventData)
    {
        _logger.LogDebug("{LogMessage} (Total: {Data})", _logMessages.StreamIsOffline, eventData.Users.Count);
        var tasks = eventData.Users.Select(user =>
            _nowStreaming.ProcessOfflineStateAsync(user, eventData.CancellationToken));

        await Task.WhenAll(tasks);

        await _nowStreaming.UpdateSummaryAsync(eventData.CancellationToken);
    }

    public async void OnStreamStateMediaRefresh(object? sender, UsersEventArguments eventData)
    {
        _logger.LogDebug("{LogMessage} (Total: {Data})", _logMessages.StreamMediaWasRefreshed, eventData.Users.Count);
        var tasks = eventData.Users.Select(user => 
                !_nowStreaming.HasEmbed(user)
                ? _nowStreaming.ProcessOnlineStateAsync(user, eventData.CancellationToken)
                : _nowStreaming.ProcessUnchangedStateAsync(user, eventData.CancellationToken, true));

        await Task.WhenAll(tasks);

        await _nowStreaming.UpdateSummaryAsync(eventData.CancellationToken);
    }

    public async void OnServiceStarting(object? sender, ServiceEventArguments eventData)
    {
        await _nowStreaming.LoadDataAsync(eventData.CancellationToken);
        await _nowStreaming.UpdateSummaryAsync(eventData.CancellationToken);
    }

    public async void OnServiceExiting(object? sender, ServiceEventArguments eventData) 
        => await _nowStreaming.UpdateSummaryAsync(eventData.CancellationToken);

    public async void OnStreamStateUnchanged(object? sender, UsersEventArguments eventData)
    {
        var tasks = eventData.Users.Select(user =>
            !_nowStreaming.HasEmbed(user)
            ? _nowStreaming.ProcessOnlineStateAsync(user, eventData.CancellationToken)
            : _nowStreaming.ProcessUnchangedStateAsync(user, eventData.CancellationToken, true));

        await Task.WhenAll(tasks);

        await _nowStreaming.UpdateSummaryAsync(eventData.CancellationToken);
        
        _logger.LogDebug("{LogMessage} (Total: {Data})", _logMessages.StreamIsUnchanged, eventData.Users.Count);
    }

    public void OnServiceStarted(object? sender, EventArgs eventData)
        => _logger.LogDebug("Service started"); // TODO: LogMessage

    public void OnServiceExited(object? sender, EventArgs eventData)
        => _logger.LogDebug("Service exited"); // TODO: LogMessage

    public void OnUserAdded(object? sender, UsersEventArguments eventData)
        => _logger.LogDebug("{Count} users added", eventData.Users.Count); // TODO: LogMessage

    public void OnUserRemoved(object? sender, UsersEventArguments eventData)
        => _logger.LogDebug("{Count} users removed", eventData.Users.Count); // TODO: LogMessage

    public void OnUserStreamError(object? sender, ErrorEventArguments eventData)
        => _logger.LogError(
            eventData.Exception,
            "UserId: {UserId}, Message: {Message}", // TODO: LogMessage
            eventData.UserId, eventData.Message);
}

// TODO: Expand upon this in the future
public class ServiceEventArguments : EventArgs
{
    public required CancellationToken CancellationToken { get; init; }
}

public class UsersEventArguments : EventArgs
{
    public List<Streamer> Users { get; set; } = [];
    public required CancellationToken CancellationToken { get; init; }
}

public class ErrorEventArguments : EventArgs
{
    public required string UserId { get; init; }
    public required string Message { get; init; }
    public required Exception Exception { get; init; }
}