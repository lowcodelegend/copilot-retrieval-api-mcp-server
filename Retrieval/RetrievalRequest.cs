namespace RetrievalApiMcpServer.Retrieval;

using System.Text.Json.Serialization;

public sealed class RetrievalRequest
{
    [JsonPropertyName("queryString")]
    public string QueryString { get; set; } = default!;

    [JsonPropertyName("dataSource")]
    public string DataSource { get; set; } = "sharePoint";

    [JsonPropertyName("resourceMetadata")]
    public string[] ResourceMetadata { get; set; } = ["title", "author"];

    [JsonPropertyName("maximumNumberOfResults")]
    public int MaximumNumberOfResults { get; set; } = 10;
}
