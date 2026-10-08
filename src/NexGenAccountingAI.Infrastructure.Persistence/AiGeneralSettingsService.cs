using NexGenAccountingAI.Application.Ai;
using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>O estado da chave da IA para o ecrã do produtor: nunca traz a chave, só se há e como acaba.</summary>
public sealed record AiKeyStatus(bool Configured, string? Hint, DateTimeOffset? UpdatedAtUtc);

/// <summary>
/// A chave da API da Anthropic na configuração geral (só o produtor). Guarda-a cifrada, com a cifra portátil, e faz valer a
/// alteração logo neste posto. Uma chave tem o formato <c>sk-ant-…</c>, sem espaços nem quebras de linha.
/// </summary>
public sealed class AiGeneralSettingsService(AccountingDbContext context, IAtSecretProtector protector, IAiApiKeyInvalidator invalidator)
{
    public async Task<AiKeyStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
        var row = await context.AiGeneralSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return row is { HasKey: true } ? new AiKeyStatus(true, row.KeyHint, row.UpdatedAtUtc) : new AiKeyStatus(false, null, row?.UpdatedAtUtc);
    }

    public async Task<AiKeyStatus> SaveKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        var key = (apiKey ?? string.Empty).Trim();
        if (key.Length < 20 || !key.StartsWith("sk-ant-", StringComparison.Ordinal) || key.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("A chave tem de começar por «sk-ant-», ter pelo menos 20 caracteres e não ter espaços: copie-a inteira da consola da Anthropic.");
        }

        var row = await context.AiGeneralSettings.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = new AiGeneralSettings(Guid.NewGuid());
            context.AiGeneralSettings.Add(row);
        }

        row.SetKey(protector.Protect(key), key);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        invalidator.Invalidate();
        return await StatusAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveKeyAsync(CancellationToken cancellationToken = default)
    {
        var row = await context.AiGeneralSettings.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (row is not { HasKey: true })
        {
            return false;
        }

        row.ClearKey();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        invalidator.Invalidate();
        return true;
    }
}

/// <summary>
/// Lê a chave da base de dados para toda a aplicação e guarda-a em memória por pouco tempo (20 s): uma alteração vale já neste
/// posto e nos outros ao fim desse tempo. Se a base não responder, usa a última chave conhecida.
/// </summary>
public sealed class DatabaseAiKeySource(IServiceScopeFactory scopes, IAtSecretProtector protector, TimeProvider time) : IAiApiKeySource, IAiApiKeyInvalidator
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(20);

    private readonly object gate = new();
    private string? cached;
    private DateTimeOffset validUntil = DateTimeOffset.MinValue;

    public void Invalidate()
    {
        lock (gate)
        {
            validUntil = DateTimeOffset.MinValue;
        }
    }

    public string? CurrentKey()
    {
        lock (gate)
        {
            if (time.GetUtcNow() < validUntil)
            {
                return cached;
            }
        }

        string? loaded = cached;
        try
        {
            using var scope = scopes.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var encrypted = context.AiGeneralSettings.AsNoTracking().Select(row => row.EncryptedApiKey).SingleOrDefault();
            loaded = string.IsNullOrEmpty(encrypted) ? null : protector.Unprotect(encrypted);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Sem base de dados (arranque, falha de rede): fica a última chave conhecida.
        }

        lock (gate)
        {
            cached = loaded;
            validUntil = time.GetUtcNow() + CacheFor;
            return cached;
        }
    }
}
