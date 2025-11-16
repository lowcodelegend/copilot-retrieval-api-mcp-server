using System.Net.Mail;
using RetrievalApiMcpServer.Retrieval;

namespace RetrievalApiMcpServer;

public sealed record GraphSettings(
    string TenantId,
    string ClientId,
    string ClientSecret,
    string? LoginHint,
    string AllowedUpn,
    string Scopes
);

public static class GraphSettingsExtensions
{
    public static void ValidateOrThrow(this GraphSettings settings, ILogger logger)
    {
        logger.Log(LogLevel.Information, "Validating Graph API settings.");
        
        if (settings is null)
            throw new InvalidOperationException("GraphSettings cannot be null.");

        // Validate GUIDs
        if (!Guid.TryParse(settings.TenantId, out _))
        {
            throw new InvalidOperationException(
                $"GRAPH_TENANT_ID is not a valid GUID: '{settings.TenantId}'");
        }

        if (!Guid.TryParse(settings.ClientId, out _))
        {
            throw new InvalidOperationException(
                $"GRAPH_CLIENT_ID is not a valid GUID: '{settings.ClientId}'");
        }

        // Validate email-like values
        void ValidateEmailIfPresent(string? value, string envVarName)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            if (!IsValidEmail(value))
            {
                throw new InvalidOperationException(
                    $"{envVarName} must be a valid email address: '{value}'");
            }
        }

        ValidateEmailIfPresent(settings.LoginHint, "GRAPH_LOGIN_HINT");
        ValidateEmailIfPresent(settings.AllowedUpn, "GRAPH_ALLOWED_UPN");

        // Validate scopes exist
        if (string.IsNullOrWhiteSpace(settings.Scopes))
        {
            throw new InvalidOperationException("GRAPH_SCOPES must not be empty.");
        }
        
        logger.LogInformation("Graph settings ok");
    }

    private static bool IsValidEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        try
        {
            var addr = new MailAddress(value);
            return addr.Address.Equals(value, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}