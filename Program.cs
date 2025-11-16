using RetrievalApiMcpServer;
using RetrievalApiMcpServer.Auth;
using RetrievalApiMcpServer.Retrieval;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using DotNetEnv;

// Try .env.local first for local dev
Env.Load(File.Exists(".env.local") ? ".env.local" : ".env");

var builder = WebApplication.CreateBuilder(args);

// --- Logging setup ---
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "HH:mm:ss ";
    options.SingleLine = true;
});

builder.Logging.SetMinimumLevel(LogLevel.Debug);

// --- end logging setup ---

var graphSettings = new GraphSettings(
    TenantId: GetRequiredEnv("GRAPH_TENANT_ID"),
    ClientId: GetRequiredEnv("GRAPH_CLIENT_ID"),
    ClientSecret: GetRequiredEnv("GRAPH_CLIENT_SECRET"),
    LoginHint: Environment.GetEnvironmentVariable("GRAPH_LOGIN_HINT"),
    AllowedUpn: GetRequiredEnv("GRAPH_ALLOWED_UPN"),
    Scopes: Environment.GetEnvironmentVariable("GRAPH_SCOPES")
             ?? "https://graph.microsoft.com/Files.Read.All https://graph.microsoft.com/Sites.Read.All offline_access"
);

builder.Services.AddSingleton(graphSettings);

// Token store: optional env override for path
var tokenPath = Environment.GetEnvironmentVariable("GRAPH_TOKEN_CACHE_PATH")
                ?? Path.Combine(AppContext.BaseDirectory, "data", "token-cache.json");
builder.Services.AddSingleton<ITokenStore>(_ => new FileTokenStore(tokenPath));

// HttpClient + GraphAuthService
builder.Services.AddHttpClient<GraphAuthService>();
builder.Services.AddHttpClient<CopilotRetrievalService>();

// MCP Server
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.Logger.LogInformation("=== Retrieval MCP server starting up ===");
graphSettings.ValidateOrThrow(app.Logger);

// /login: redirect user to Azure AD sign-in
app.MapGet("/login", (HttpContext httpContext, GraphAuthService auth) =>
{
    // Build redirectUri that Azure AD will call back to
    var redirectUri = GetRedirectUri(httpContext);

    // Simple random state; in a more complex setup you could persist & validate this
    var state = Guid.NewGuid().ToString("N");

    var authorizeUrl = auth.GetAuthorizeUrl(redirectUri, state);

    return Results.Redirect(authorizeUrl);
});

/// /auth/callback: Azure AD redirects here with ?code=...
app.MapGet("/auth/callback", async (
    HttpRequest request,
    GraphAuthService auth,
    GraphSettings settings,
    CancellationToken ct) =>
{
    var query = request.Query;

    // Handle error cases from Azure AD (e.g. user cancelled)
    if (!string.IsNullOrEmpty(query["error"]))
    {
        var error = query["error"].ToString();
        var description = query["error_description"].ToString();
        var htmlError = $"""
            <html>
            <body>
                <h2>Authentication failed</h2>
                <p><strong>Error:</strong> {System.Net.WebUtility.HtmlEncode(error)}</p>
                <p><strong>Description:</strong> {System.Net.WebUtility.HtmlEncode(description)}</p>
            </body>
            </html>
            """;

        return Results.Content(htmlError, "text/html");
    }

    var code = query["code"].ToString();
    if (string.IsNullOrWhiteSpace(code))
    {
        return Results.BadRequest("Missing 'code' query parameter.");
    }

    var redirectUri = GetRedirectUri(request.HttpContext);

    try
    {
        // Exchange the authorization code for tokens and save to file
        await auth.ExchangeCodeForTokenAsync(code, redirectUri, ct);

        var html = """
            <html>
            <body>
                <h2>Authentication successful</h2>
                <p>You can now return to your MCP client or chat window and try your request again.</p>
            </body>
            </html>
            """;

        return Results.Content(html, "text/html");
    }
    catch (InvalidOperationException ex) when (
        ex.Message.StartsWith("Authenticated user is not allowed", StringComparison.OrdinalIgnoreCase))
    {
        // User logged in with the wrong account (UPN mismatch)
        var allowedUpn = settings.AllowedUpn ?? "the designated service account";

        var htmlError = $"""
            <html>
            <body>
                <h2>Wrong account used</h2>
                <p>This MCP server is locked to a specific Microsoft 365 account for security reasons.</p>
                <p>Please sign out in this browser and sign in again using:</p>
                <p><code>{System.Net.WebUtility.HtmlEncode(allowedUpn)}</code></p>
                <p>After signing in with the correct account, return to your MCP client or chat window and try your request again.</p>
            </body>
            </html>
            """;

        return Results.Content(htmlError, "text/html");
    }
    catch (Exception ex)
    {
        var htmlError = $"""
            <html>
            <body>
                <h2>Authentication error</h2>
                <p>{System.Net.WebUtility.HtmlEncode(ex.Message)}</p>
            </body>
            </html>
            """;

        return Results.Content(htmlError, "text/html");
    }
});



// Health endpoint (same as before, maybe add token file info)
app.MapGet("/health", (GraphSettings settings) =>
{
    return Results.Ok(new
    {
        status = "ok",
        graphTenantIdConfigured = !string.IsNullOrWhiteSpace(settings.TenantId),
        graphClientIdConfigured = !string.IsNullOrWhiteSpace(settings.ClientId),
        graphClientSecretConfigured = !string.IsNullOrWhiteSpace(settings.ClientSecret),
        loginHintConfigured = !string.IsNullOrWhiteSpace(settings.LoginHint),
        scopes = settings.Scopes
    });
});

// Optional debug endpoint to see token presence (no secrets)
app.MapGet("/debug/token-status", async (GraphAuthService auth, CancellationToken ct) =>
{
    var token = await auth._tokenStore.LoadAsync(ct);
    
    return Results.Ok(new
    {
        hasToken = token is not null,
        expired = token?.IsExpired(),
        expiresAtUtc = token?.ExpiresAtUtc
    });
});

app.MapPost("/debug/retrieval", async (
    RetrievalRequest req,
    CopilotRetrievalService retrieval,
    CancellationToken ct) =>
{
    try
    {
        var result = await retrieval.SearchAsync(req, ct);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex) when (ex.Message == "auth_required")
    {
        return Results.Unauthorized();
    }
});

app.MapMcp("/mcp");
app.Run();

static string GetRequiredEnv(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException(
            $"Environment variable '{name}' is required but was not set.");
    }

    return value;
}

static string GetRedirectUri(HttpContext httpContext)
{
    // Optional explicit override
    var overrideUri = Environment.GetEnvironmentVariable("GRAPH_REDIRECT_URI");
    if (!string.IsNullOrWhiteSpace(overrideUri))
    {
        return overrideUri;
    }

    var request = httpContext.Request;
    // e.g. http://localhost:5192/auth/callback
    return $"{request.Scheme}://{request.Host}{request.PathBase}/auth/callback";
}