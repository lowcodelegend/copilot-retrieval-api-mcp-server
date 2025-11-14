namespace RetrievalApiMcpServer.Auth;

public interface ITokenStore
{
    Task<TokenInfo?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(TokenInfo token, CancellationToken cancellationToken = default);
}