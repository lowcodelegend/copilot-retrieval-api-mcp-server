using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RetrievalApiMcpServer.Auth;

public class GraphAuthService
{
    private readonly HttpClient _httpClient;
    private readonly GraphSettings _settings;
    public readonly ITokenStore _tokenStore;
    private readonly ILogger<GraphAuthService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GraphAuthService(HttpClient httpClient, GraphSettings settings, ITokenStore tokenStore, ILogger<GraphAuthService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    private string AuthorityBase =>
        $"https://login.microsoftonline.com/{_settings.TenantId}/oauth2/v2.0";

    /// <summary>
    /// Builds the Azure AD authorize URL used by /login.
    /// </summary>
    public string GetAuthorizeUrl(string redirectUri, string state)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = _settings.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["response_mode"] = "query",
            ["scope"] = _settings.Scopes,
            ["state"] = state
        };

        // login_hint is optional UX sugar only; not a security boundary.
        if (!string.IsNullOrWhiteSpace(_settings.LoginHint))
        {
            query["login_hint"] = _settings.LoginHint;
        }

        var sb = new StringBuilder($"{AuthorityBase}/authorize?");
        sb.Append(string.Join("&", query
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value!)}")));

        return sb.ToString();
    }

    /// <summary>
    /// Try to get a valid access token from cache, refreshing if needed.
    /// Returns null if there is no token at all or refresh fails.
    /// </summary>
    public async Task<TokenInfo?> TryGetValidTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = await _tokenStore.LoadAsync(cancellationToken);
        if (token is null)
        {
            return null;
        }

        // Enforce that the cached token still belongs to the allowed UPN
        if (!IsTokenForAllowedUser(token.AccessToken))
        {
            // Optionally clear cache here if you add a ClearAsync on ITokenStore
            return null;
        }

        if (!token.IsExpired())
        {
            return token;
        }

        // Try to refresh
        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            return null;
        }

        var refreshed = await RefreshAsync(token.RefreshToken, cancellationToken);
        if (refreshed is null)
        {
            return null;
        }

        await _tokenStore.SaveAsync(refreshed, cancellationToken);
        return refreshed;
    }

    /// <summary>
    /// Exchange an authorization code for tokens and save them.
    /// Used by /auth/callback.
    /// </summary>
    public async Task<TokenInfo> ExchangeCodeForTokenAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", _settings.ClientId),
            new KeyValuePair<string, string>("client_secret", _settings.ClientSecret),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
            new KeyValuePair<string, string>("scope", _settings.Scopes)
        });

        var response = await _httpClient.PostAsync($"{AuthorityBase}/token", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Token endpoint returned {(int)response.StatusCode}: {body}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions)
                           ?? throw new InvalidOperationException("Failed to deserialize token response.");

        var tokenInfo = tokenResponse.ToTokenInfo();

        // Enforce that the user who just signed in is the one we expect
        _logger.LogInformation("Validating allowed graph user");
        if (!IsTokenForAllowedUser(tokenInfo.AccessToken))
        {
            throw new InvalidOperationException(
                "Authenticated user is not allowed. " +
                "Please sign in using the designated account for this MCP server. As per GRAPH_ALLOWED_UPN");
        }
        _logger.LogInformation("Graph user matches, ok.");

        await _tokenStore.SaveAsync(tokenInfo, cancellationToken);
        return tokenInfo;
    }

    private async Task<TokenInfo?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", _settings.ClientId),
            new KeyValuePair<string, string>("client_secret", _settings.ClientSecret),
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
            new KeyValuePair<string, string>("scope", _settings.Scopes)
        });

        var response = await _httpClient.PostAsync($"{AuthorityBase}/token", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Caller will treat this as "auth required"
            return null;
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions);
        if (tokenResponse is null)
        {
            return null;
        }

        var tokenInfo = tokenResponse.ToTokenInfo();

        // 👇 Also enforce identity on refresh
        if (!IsTokenForAllowedUser(tokenInfo.AccessToken))
        {
            // Do not accept a refreshed token for the wrong user
            return null;
        }

        return tokenInfo;
    }

    /// <summary>
    /// Checks the access token's UPN/username against GRAPH_ALLOWED_UPN (if set).
    /// </summary>
    private bool IsTokenForAllowedUser(string accessToken)
    {
        var handler = new JwtSecurityTokenHandler();
        JwtSecurityToken jwt;

        try
        {
            jwt = handler.ReadJwtToken(accessToken);
        }
        catch
        {
            _logger.LogError("Failed to crack JWT to check graph user.");
            return false;
        }

        // Try common claim types: upn, preferred_username, email
        var upn =
            jwt.Claims.FirstOrDefault(c => c.Type == "upn")?.Value ??
            jwt.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value ??
            jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value;

        if (string.IsNullOrWhiteSpace(upn))
        {
            return false;
        }

        return string.Equals(upn, _settings.AllowedUpn, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TokenResponse
    {
        public string Access_Token { get; set; } = default!;
        public string Refresh_Token { get; set; } = default!;
        public int Expires_In { get; set; } // seconds

        public TokenInfo ToTokenInfo()
        {
            var now = DateTimeOffset.UtcNow;
            return new TokenInfo
            {
                AccessToken = Access_Token,
                RefreshToken = Refresh_Token,
                ExpiresAtUtc = now.AddSeconds(Expires_In > 0 ? Expires_In : 3600)
            };
        }
    }
}
