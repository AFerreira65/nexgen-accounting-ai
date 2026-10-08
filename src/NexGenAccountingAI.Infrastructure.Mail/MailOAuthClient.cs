using System.Text;
using System.Text.Json;
using NexGenAccountingAI.Application.Mail;
using NexGenAccountingAI.Domain.Mail;

namespace NexGenAccountingAI.Infrastructure.Mail;

/// <summary>
/// O fluxo OAuth2 com a Microsoft (Entra ID) e a Google para SMTP e IMAP. O utilizador faz o login — e o MFA —
/// na página do fornecedor; a aplicação só recebe um token de renovação, que guarda cifrado.
/// Nunca foi exercitado contra os fornecedores reais nesta máquina: os pedidos são verificados nos testes com um servidor simulado.
/// </summary>
public sealed class MailOAuthClient(HttpClient http) : IMailOAuthClient
{
    internal const string MicrosoftScopes = "offline_access openid email https://outlook.office.com/SMTP.Send https://outlook.office.com/IMAP.AccessAsUser.All";
    internal const string GoogleScopes = "openid email https://mail.google.com/";

    public Uri AuthorizationUrl(MailProvider provider, MailOAuthApp app, string redirectUri, string state, string? loginHint)
    {
        ArgumentNullException.ThrowIfNull(app);
        var query = new Dictionary<string, string>
        {
            ["client_id"] = app.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
        };
        if (!string.IsNullOrWhiteSpace(loginHint))
        {
            query["login_hint"] = loginHint;
        }

        string baseUrl;
        switch (provider)
        {
            case MailProvider.Microsoft:
                baseUrl = $"https://login.microsoftonline.com/{Tenant(app)}/oauth2/v2.0/authorize";
                query["scope"] = MicrosoftScopes;
                query["response_mode"] = "query";
                query["prompt"] = "select_account";
                break;
            case MailProvider.Google:
                baseUrl = "https://accounts.google.com/o/oauth2/v2/auth";
                query["scope"] = GoogleScopes;
                query["access_type"] = "offline";
                query["prompt"] = "consent";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), "Só a Microsoft e a Google usam OAuth2.");
        }

        return new Uri($"{baseUrl}?{string.Join('&', query.Select(item => $"{item.Key}={Uri.EscapeDataString(item.Value)}"))}");
    }

    public Task<MailOAuthTokens> ExchangeCodeAsync(MailProvider provider, MailOAuthApp app, string redirectUri, string code, CancellationToken cancellationToken = default) =>
        RequestAsync(provider, app, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
        }, null, cancellationToken);

    public Task<MailOAuthTokens> RefreshAsync(MailProvider provider, MailOAuthApp app, string refreshToken, CancellationToken cancellationToken = default) =>
        RequestAsync(provider, app, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, refreshToken, cancellationToken);

    private async Task<MailOAuthTokens> RequestAsync(
        MailProvider provider, MailOAuthApp app, Dictionary<string, string> form, string? previousRefreshToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(app);
        form["client_id"] = app.ClientId;
        form["client_secret"] = app.ClientSecret;
        if (provider == MailProvider.Microsoft)
        {
            form["scope"] = MicrosoftScopes;
        }

        var url = provider switch
        {
            MailProvider.Microsoft => $"https://login.microsoftonline.com/{Tenant(app)}/oauth2/v2.0/token",
            MailProvider.Google => "https://oauth2.googleapis.com/token",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), "Só a Microsoft e a Google usam OAuth2."),
        };

        using var response = await http.PostAsync(url, new FormUrlEncodedContent(form), cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        var root = json.RootElement;
        if (!response.IsSuccessStatusCode || !root.TryGetProperty("access_token", out var accessToken))
        {
            var description = root.TryGetProperty("error_description", out var detail) ? detail.GetString() : root.TryGetProperty("error", out var error) ? error.GetString() : null;
            throw new InvalidOperationException($"O fornecedor de email recusou o pedido ({(int)response.StatusCode}){(description is null ? string.Empty : $": {description.Split('\n')[0]}")}.");
        }

        var expiresIn = root.TryGetProperty("expires_in", out var seconds) && seconds.TryGetInt32(out var value) ? value : 3600;
        var refresh = root.TryGetProperty("refresh_token", out var refreshToken) ? refreshToken.GetString() : previousRefreshToken;
        var email = root.TryGetProperty("id_token", out var idToken) ? EmailFromIdToken(idToken.GetString()) : null;
        return new MailOAuthTokens(accessToken.GetString()!, refresh, DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60), email);
    }

    private static string Tenant(MailOAuthApp app) => string.IsNullOrWhiteSpace(app.Tenant) ? "common" : app.Tenant.Trim();

    /// <summary>O email do utilizador, lido do <c>id_token</c> (só informativo: a identidade vem do fornecedor por HTTPS).</summary>
    internal static string? EmailFromIdToken(string? idToken)
    {
        var parts = idToken?.Split('.');
        if (parts is not { Length: >= 2 })
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            foreach (var name in new[] { "email", "preferred_username", "upn" })
            {
                if (json.RootElement.TryGetProperty(name, out var value) && value.GetString() is { Length: > 0 } email)
                {
                    return email;
                }
            }
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }

        return null;
    }
}
