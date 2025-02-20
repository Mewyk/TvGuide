using System.Text.Json;

using Microsoft.Extensions.Options;

namespace TvGuide.Modules;

public class DataModule(IOptions<Configuration> settings)
{
    private readonly string _filePath = settings.Value.NowLive.UserDataFile;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<List<TwitchStreamer>> LoadUsersAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath)) return [];

            using var stream = File.OpenRead(_filePath);
            if (stream.Length == 0) return [];

            var users = await JsonSerializer.DeserializeAsync<List<TwitchStreamer>>(stream, cancellationToken: cancellationToken);
            return users ?? [];
        }
        finally { _semaphore.Release(); }
    }

    public async Task SaveUsersAsync(IEnumerable<TwitchStreamer> users, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await JsonSerializer.SerializeAsync(
                File.Create(_filePath), users,
                cancellationToken: cancellationToken);
        }
        finally { _semaphore.Release(); }
    }
}