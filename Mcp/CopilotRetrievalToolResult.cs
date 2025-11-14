using RetrievalApiMcpServer.Retrieval;

namespace RetrievalApiMcpServer.Mcp;

public sealed class CopilotRetrievalToolResult
{
    /// <summary>
    /// "ok" when results are present, "auth_required" when user must sign in first.
    /// </summary>
    public string Status { get; set; } = "ok";

    /// <summary>
    /// Natural language instructions intended for the model.
    /// </summary>
    public string? MessageForModel { get; set; }

    /// <summary>
    /// True if the user must re-authenticate in a browser.
    /// </summary>
    public bool AuthRequired { get; set; }

    /// <summary>
    /// URL the user should open in a browser to sign in (when AuthRequired is true).
    /// </summary>
    public string? AuthUrl { get; set; }

    /// <summary>
    /// Retrieval results (webUrl + extracts). Empty when AuthRequired is true.
    /// </summary>
    public List<RetrievalResultItem> Results { get; set; } = new();
}
