using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TvGuide.Models;

namespace TvGuide.Modules;

/// <summary>
/// Thread-safe persistence for tracked user data.
/// </summary>
public sealed class DataModule(
    IOptions<Configuration> settings,
    ILogger<DataModule> logger) : IDisposable
{
    private readonly string _filePath = settings.Value.NowLive.UserDataFile;
    private readonly ILogger<DataModule> _logger = logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Loads tracked streamers, or empty list if file doesn't exist.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the load operation.</param>
    /// <returns>The persisted tracked users, or an empty list when no data exists.</returns>
    public async Task<List<TrackedTwitchUser>> LoadUsersAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath)) 
                return [];

            await using var stream = File.OpenRead(_filePath);
            if (stream.Length == 0) 
                return [];

            var users = await JsonSerializer.DeserializeAsync<List<TrackedTwitchUser>>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return users ?? [];
        }
        catch (JsonException exception)
        {
            DataModuleLog.FailedToLoadUsers(_logger, exception, _filePath);
            return [];
        }
        catch (NotSupportedException exception)
        {
            DataModuleLog.FailedToLoadUsers(_logger, exception, _filePath);
            return [];
        }
        finally 
        { 
            _semaphore.Release(); 
        }
    }

    /// <summary>
    /// Saves tracked streamers.
    /// </summary>
    /// <param name="users">Users to persist to the configured file.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the save operation.</param>
    public async Task SaveUsersAsync(IEnumerable<TrackedTwitchUser> users, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, users, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally 
        { 
            _semaphore.Release(); 
        }
    }

    /// <summary>
    /// Releases the semaphore used to serialize user-data persistence.
    /// </summary>
    public void Dispose() => _semaphore.Dispose();
}