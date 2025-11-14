namespace RetrievalApiMcpServer.Auth;

public sealed class TokenInfo
{
    public string AccessToken { get; init; } = default!;
    public string RefreshToken { get; init; } = default!;
    public DateTimeOffset ExpiresAtUtc { get; init; }

    public bool IsExpired(TimeSpan? skew = null)
    {
        var s = skew ?? TimeSpan.FromMinutes(5);
        return DateTimeOffset.UtcNow >= ExpiresAtUtc - s;
    }
}