using System.ComponentModel;
using ModelContextProtocol.Server;
using RetrievalApiMcpServer.Retrieval;

namespace RetrievalApiMcpServer.Mcp;

[McpServerToolType]
public sealed class CopilotTools
{
    private readonly CopilotRetrievalService _retrieval;

    public CopilotTools(CopilotRetrievalService retrieval)
    {
        _retrieval = retrieval;
    }

    [McpServerTool(Name = "copilotRetrievalSearch")]
    [Description("Search Microsoft 365 via the Copilot Retrieval API. If authentication is required, this tool will return an authRequired flag and an authUrl; in that case, you must tell the user to open the URL in a browser and sign in before calling the tool again.")]
    public async Task<CopilotRetrievalToolResult> CopilotRetrievalSearchAsync(
        [Description("Natural language search query. Provide relevant keywords only for best results")]
        string queryString,

        [Description("Data source to search. Use sharePoint only.")]
        string? dataSource = "sharePoint",

        [Description("Maximum number of results to request (1-10).")]
        int maximumNumberOfResults = 10,
        CancellationToken ct = default)
    {
        // Build the internal request model
        var request = new RetrievalRequest
        {
            QueryString = queryString,
            DataSource = string.IsNullOrWhiteSpace(dataSource) ? "sharePoint" : dataSource,
            MaximumNumberOfResults = maximumNumberOfResults <= 0 ? 10 : maximumNumberOfResults,
            ResourceMetadata = Array.Empty<string>() // not used for now
        };

        try
        {
            var response = await _retrieval.SearchAsync(request, ct);

            return new CopilotRetrievalToolResult
            {
                Status = "ok",
                AuthRequired = false,
                AuthUrl = null,
                MessageForModel = "Search completed successfully. Use these results (webUrl and extracts) to answer the user's question directly. Do not call this tool again unless you need a different query.",
                Results = response.RetrievalHits
            };
        }
        catch (InvalidOperationException ex) when (ex.Message == "auth_required")
        {
            var baseUrl =
                Environment.GetEnvironmentVariable("MCP_PUBLIC_BASE_URL")
                ?? Environment.GetEnvironmentVariable("PUBLIC_BASE_URL")
                ?? "http://localhost:5192";

            var authUrl = $"{baseUrl.TrimEnd('/')}/login";

            return new CopilotRetrievalToolResult
            {
                Status = "auth_required",
                AuthRequired = true,
                AuthUrl = authUrl,
                Results = new List<RetrievalResultItem>(),
                MessageForModel =
                    $"Authentication to Microsoft 365 is required before this tool can be used.\n\n" +
                    $"DO NOT call the 'copilotRetrievalSearch' tool again until the user has authenticated.\n\n" +
                    $"Instead, clearly tell the user something like:\n\n" +
                    $"\"To continue, please open this link in your browser and sign in: {authUrl}\n" +
                    $"After you’ve finished signing in, tell me and I will try the search again.\"\n\n" +
                    $"Once the user confirms they’ve completed sign-in, you may call this tool again with the same or updated query."
            };
        }
    }
}
