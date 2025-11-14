namespace RetrievalApiMcpServer;

public sealed record GraphSettings(
    string TenantId,
    string ClientId,
    string ClientSecret,
    string? LoginHint,
    string Scopes
);