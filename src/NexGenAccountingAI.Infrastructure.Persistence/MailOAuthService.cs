using System.Text.Json;
using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Application.Mail;
using NexGenAccountingAI.Domain.Mail;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>O estado de uma aplicação OAuth2 (Microsoft ou Google) na configuração do produtor; nunca traz o segredo.</summary>
public sealed record MailOAuthAppView(string Provider, bool Configured, string? ClientId, string? Tenant, bool HasSecret, DateTimeOffset? UpdatedAtUtc);

public sealed record MailOAuthReturn(MailAccountPurpose Purpose, Guid OwnerId, MailProvider Provider);

/// <summary>
/// O OAuth2 do email (ADR-0019). O produtor regista a aplicação no Azure ou na Google Cloud e guarda aqui o ID
/// e o segredo; cada utilizador (ou o administrador, para a caixa do gabinete) autoriza com o seu login — e o seu MFA —
/// e a aplicação guarda só o token de renovação, cifrado. O <c>state</c> do pedido é cifrado e expira em 15 minutos,
/// e só vale para o utilizador que o pediu, o que impede ligar a conta de outra pessoa.
/// </summary>
public sealed class MailOAuthService(
    AccountingDbContext context,
    IAtSecretProtector protector,
    IMailOAuthClient oauth,
    MailAccountService accounts,
    IDataProtectionProvider dataProtection)
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

    private sealed record State(MailAccountPurpose Purpose, Guid OwnerId, MailProvider Provider, Guid UserId);

    private ITimeLimitedDataProtector Protector => dataProtection.CreateProtector("mail-oauth-state").ToTimeLimitedDataProtector();

    public async Task<IReadOnlyList<MailOAuthAppView>> AppsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.MailOAuthApps.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return new[] { MailProvider.Microsoft, MailProvider.Google }
            .Select(provider => rows.SingleOrDefault(item => item.Provider == provider) is { } row
                ? new MailOAuthAppView(provider.ToString(), row.IsConfigured, row.ClientId, row.Tenant, !string.IsNullOrEmpty(row.ClientSecret), row.UpdatedAtUtc)
                : new MailOAuthAppView(provider.ToString(), false, null, null, false, null))
            .ToList();
    }

    /// <summary>Guarda a aplicação OAuth2 de um fornecedor; um segredo vazio mantém o que já estava.</summary>
    public async Task SaveAppAsync(MailProvider provider, string clientId, string? clientSecret, string? tenant, CancellationToken cancellationToken = default)
    {
        var row = await context.MailOAuthApps.SingleOrDefaultAsync(item => item.Provider == provider, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = new MailOAuthAppSettings(provider);
            context.MailOAuthApps.Add(row);
        }

        row.Update(clientId, string.IsNullOrEmpty(clientSecret) ? null : protector.Protect(clientSecret), tenant);
        if (!row.IsConfigured)
        {
            throw new InvalidOperationException("Indique também o segredo do cliente.");
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveAppAsync(MailProvider provider, CancellationToken cancellationToken = default)
    {
        var row = await context.MailOAuthApps.SingleOrDefaultAsync(item => item.Provider == provider, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        context.MailOAuthApps.Remove(row);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>O endereço da página de login do fornecedor.</summary>
    public async Task<Uri> StartAsync(
        MailAccountPurpose purpose, Guid ownerId, MailProvider provider, Guid userId, string redirectUri, string? loginHint, CancellationToken cancellationToken = default)
    {
        var app = await LoadAppAsync(provider, cancellationToken).ConfigureAwait(false);
        var state = Protector.Protect(JsonSerializer.Serialize(new State(purpose, ownerId, provider, userId)), StateLifetime);
        return oauth.AuthorizationUrl(provider, app, redirectUri, state, loginHint);
    }

    /// <summary>Conclui a autorização: troca o código por tokens e guarda o de renovação na conta.</summary>
    public async Task<MailOAuthReturn> CompleteAsync(string state, string code, Guid sessionUserId, string redirectUri, CancellationToken cancellationToken = default)
    {
        State parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<State>(Protector.Unprotect(state)) ?? throw new System.Security.Cryptography.CryptographicException();
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or JsonException)
        {
            throw new InvalidOperationException("O pedido de autorização expirou ou não é válido: volte a ligar a conta.");
        }

        if (parsed.UserId != sessionUserId)
        {
            throw new InvalidOperationException("Este pedido de autorização é de outro utilizador.");
        }

        var app = await LoadAppAsync(parsed.Provider, cancellationToken).ConfigureAwait(false);
        var tokens = await oauth.ExchangeCodeAsync(parsed.Provider, app, redirectUri, code, cancellationToken).ConfigureAwait(false);
        await accounts.StoreOAuthAsync(parsed.Purpose, parsed.OwnerId, parsed.Provider, tokens, cancellationToken).ConfigureAwait(false);
        return new MailOAuthReturn(parsed.Purpose, parsed.OwnerId, parsed.Provider);
    }

    private async Task<MailOAuthApp> LoadAppAsync(MailProvider provider, CancellationToken cancellationToken)
    {
        var row = await context.MailOAuthApps.AsNoTracking().SingleOrDefaultAsync(item => item.Provider == provider, cancellationToken).ConfigureAwait(false);
        if (row is null || !row.IsConfigured)
        {
            throw new InvalidOperationException($"A ligação {(provider == MailProvider.Microsoft ? "à Microsoft" : "à Google")} ainda não está preparada: o produtor da aplicação tem de registar a aplicação OAuth.");
        }

        return new MailOAuthApp(row.ClientId, protector.Unprotect(row.ClientSecret), row.Tenant);
    }
}
