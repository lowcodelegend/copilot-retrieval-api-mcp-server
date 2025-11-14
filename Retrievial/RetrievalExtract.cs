namespace RetrievalApiMcpServer.Retrieval;

public sealed class RetrievalExtract
{
    public string Text { get; set; } = default!;
    public double RelevanceScore { get; set; }
}