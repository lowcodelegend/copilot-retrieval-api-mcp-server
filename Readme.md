# Microsoft CoPilot Retrieval API MCP Server

### Description

Since mid-2025 Microsoft has provided a Retrieval API for Copilot.  

The API leverages Semantic Search and automatically takes care of OneDrive and SharePoint document chunking, embedding, and storing vector representation of these documents.  This saves the engineer from needing to bring their own Embedding Models, Vector DB, Chunker, and upsert flow.  

It allows the central data-store to remain as M365. Rather than copying the data elsewhere.

For information on the API see: [Retrieval API Documentation](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/api/ai-services/retrieval/copilotroot-retrieval?pivots=graph-v1)

The MCP server provides the following interface:
Input is a natural language query.
Response is a list of chunks (document fragments) of relevant text from OneDrive/SharePoint Documents.

### Getting Started

1. Do an App Registration in Entra ID (AAD).
2. Set-up the required Environment Variables.
3. Host and run on Docker with docker compose.

```
cd RetrievalApiMcpServer
docker compose up -d
```

4. Visit the /login endpoint to setup to authenticate to Graph API.
5. Add the server to your mcp.json or other MCP client:

```
{
  "mcpServers": {
    "copilotRetrieval": {
      "url": "http://localhost:5192/mcp"
    }
  }
}
```

### Environment Variables

You will need an App Registration in Entra ID (AAD). Update the values in the .env file

Default Docker ports exposed are:
HTTP: 5192
HTTPS: 7198

No provisions has been added yet to manage the SSL cert.  TODO.
Typically the service is expected to run behind an SSL offloading Reverse Proxy.

```
GRAPH_TENANT_ID=REPLACEME                                       # from Entra ID App Registration
GRAPH_CLIENT_ID=REPLACEME                                       #
GRAPH_CLIENT_SECRET=REPLACEME                                   #
GRAPH_LOGIN_HINT=me@myO365Tenant.com                            # user login hint
GRAPH_REDIRECT_URI=http://localhost:5192/auth/callback          # e.g. https://myMcpServerUrl/auth/callback, make sure it's registered in App Registration in Entra ID
MOCK_COPILOT_RETRIEVAL=0                                        # for offline hacking, mock response from graph and bypass auth
PUBLIC_BASE_URL=https://myMcpServerUrl                          # if using a reverse proxy you can set this
```
### Notes on Authentication

Copilot Retrieval API only supports Auth Code tokens which need a user context.  No AppOnly/ClientCredentials

* When you authenticate, you will provide a single static user: user@myTenant.com
* The user must have a FULL Copilot licence, not just a Copilot Chat licence, but only that one user requires it.
* The refresh token has a 90 day inactivity window by default Entra ID (AAD) settings.
* If nobody uses the server in that inactivity window, you will need to re-auth.
* Re-auth is done by the copilot licenced user going to /login on the MCP server. You can trap the auth_required error in the Agent and fire off notifications etc.
* Be careful, all users of the MCP server will have access to whatever the user has access to.

### Notes on Rate Limits

As there's a single user context, care should be taken to understand the rate limiting restrictions MS places.

### Licence and Copyright

MIT
Ashley Evans 2025

