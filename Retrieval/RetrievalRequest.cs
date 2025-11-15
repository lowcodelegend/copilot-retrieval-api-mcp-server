namespace RetrievalApiMcpServer.Retrieval;

public sealed class RetrievalRequest
{
    public string QueryString { get; set; } = default!;
    public string DataSource { get; set; } = "sharePoint";
    
    public string[] ResourceMetadata { get; set; } = Array.Empty<string>();
    public int MaximumNumberOfResults { get; set; } = 10;
}