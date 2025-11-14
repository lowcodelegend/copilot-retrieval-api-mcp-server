namespace RetrievalApiMcpServer.Retrieval;

public sealed class RetrievalResultItem
{
    public string WebUrl { get; set; } = default!;
    public List<RetrievalExtract> Extracts { get; set; } = new();
}