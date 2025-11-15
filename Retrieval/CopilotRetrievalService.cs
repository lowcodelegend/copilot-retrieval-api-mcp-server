using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RetrievalApiMcpServer.Auth;

namespace RetrievalApiMcpServer.Retrieval;

public sealed class CopilotRetrievalService
{
    private readonly HttpClient _httpClient;
    private readonly GraphAuthService _auth;
    private readonly bool _mockEnabled;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CopilotRetrievalService(HttpClient httpClient, GraphAuthService auth)
    {
        _httpClient = httpClient;
        _auth = auth;
        _mockEnabled = Environment.GetEnvironmentVariable("MOCK_COPILOT_RETRIEVAL") == "1";
    }

    public async Task<RetrievalResponse> SearchAsync(
        RetrievalRequest request,
        CancellationToken ct = default)
    {
        // MOCK MODE
        if (_mockEnabled)
        {
            
            return MakeMockResponse(request);
        }

        // LIVE MODE
        TokenInfo? token;
        // TODO: This is a bit of a quick hack for local offline dev - if the token refresh fails, or any other part of the auth chain, consider that an auth failure
        try
        {
            token = await _auth.TryGetValidTokenAsync(ct);
        }
        catch
        {
            token = null;
        }

        if (token is null)
        {
            // Caller will translate this into MCP "auth_required"
            Console.WriteLine("Failed to get Token.  Require Auth.");
            throw new InvalidOperationException("auth_required");
        }

        using var msg = new HttpRequestMessage(HttpMethod.Post,
            "https://graph.microsoft.com/v1.0/copilot/retrieval");

        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        msg.Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var resp = await _httpClient.SendAsync(msg, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Graph returned {(int)resp.StatusCode}: {body}");
        }

        var result = JsonSerializer.Deserialize<RetrievalResponse>(body, JsonOptions);

        return result ?? new RetrievalResponse();
    }

    private static RetrievalResponse MakeMockResponse(RetrievalRequest req)
    {
        //Mock some dummy results for offline hacking
        return new RetrievalResponse
        {
            RetrievalHits = new List<RetrievalResultItem>
            {
                new RetrievalResultItem
                {
                    WebUrl = "https://mock.contoso.com/vpn/setup.docx",
                    Extracts = new List<RetrievalExtract>
                    {
                        new RetrievalExtract
                        {
                            Text          = $"Mock extract for '{req.QueryString}' #1. If mock data is being used for simulations you MUST tell the user that it's the case!!",
                            RelevanceScore = 0.95
                        },
                        new RetrievalExtract
                        {
                            Text          = $"Mock extract for '{req.QueryString}' #2.  If mock data is being used for simulations you MUST tell the user that it's the case!!",
                            RelevanceScore = 0.88
                        }
                    }
                },

                new RetrievalResultItem
                {
                    WebUrl = "https://mock.contoso.com/vpn/troubleshoot.docx",
                    Extracts = new List<RetrievalExtract>
                    {
                        new RetrievalExtract
                        {
                            Text          = $"Troubleshooting mock for '{req.QueryString}'",
                            RelevanceScore = 0.81
                        }
                    }
                }
            }
        };
    }

}
