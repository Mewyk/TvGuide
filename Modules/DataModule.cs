using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TvGuide.Modules;

/// <summary>
/// Thread-safe persistence for tracked user data.
/// </summary>
public sealed class DataModule(IOptions<Configuration> settings) : IDisposable
{
    private readonly string _filePath = settings.Value.NowLive.UserDataFile;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Loads tracked streamers, or empty list if file doesn't exist.
    /// </summary>
    public async Task<List<TwitchStreamer>> LoadUsersAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath)) 
                return [];

            await using var stream = File.OpenRead(_filePath);
            if (stream.Length == 0) 
                return [];

            var users = await JsonSerializer.DeserializeAsync<List<TwitchStreamer>>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return users ?? [];
        }
        finally 
        { 
            _semaphore.Release(); 
        }
    }

    /// <summary>
    /// Saves tracked streamers.
    /// </summary>
    public async Task SaveUsersAsync(IEnumerable<TwitchStreamer> users, CancellationToken cancellationToken)
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

    public void Dispose() => _semaphore.Dispose();
}