using System.Text.Json;

namespace RetrievalApiMcpServer.Auth;

public sealed class FileTokenStore : ITokenStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public FileTokenStore(string path)
    {
        _path = path;
    }

    public async Task<TokenInfo?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            await using var stream = File.OpenRead(_path);
            var token = await JsonSerializer.DeserializeAsync<TokenInfo>(stream, JsonOptions, cancellationToken);
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(TokenInfo token, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.Create(_path);
            await JsonSerializer.SerializeAsync(stream, token, JsonOptions, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }
}