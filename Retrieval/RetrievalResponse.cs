namespace RetrievalApiMcpServer.Retrieval;

public sealed class RetrievalResponse
{
    public List<RetrievalResultItem> RetrievalHits { get; set; } = new();
}