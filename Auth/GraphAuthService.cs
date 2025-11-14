using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RetrievalApiMcpServer.Auth;

public class GraphAuthService
{
    private readonly HttpClient _httpClient;
    private readonly GraphSettings _settings;
    public readonly ITokenStore _tokenStore;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GraphAuthService(HttpClient httpClient, GraphSettings settings, ITokenStore tokenStore)
    {
        _httpClient = httpClient;
        _settings = settings;
        _tokenStore = tokenStore;
    }

    private string AuthorityBase =>
        $"https://login.microsoftonline.com/{_settings.TenantId}/oauth2/v2.0";

    /// <summary>
    /// Builds the Azure AD authorize URL used by /login (we’ll call this later).
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
    /// Used later by /auth/callback.
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
            // We deliberately return null instead of throwing – caller will treat this
            // as "auth required" and trigger the browser flow.
            return null;
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions);
        return tokenResponse?.ToTokenInfo();
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
