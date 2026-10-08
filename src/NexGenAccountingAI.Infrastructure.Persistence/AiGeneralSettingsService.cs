using NexGenAccountingAI.Application.Ai;
using NexGenAccountingAI.Application.At;
using NexGenAccountingAI.Domain.Organizations;
using NexGenAccountingAI.Domain.Reporting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>A franquia por omissão, os modelos por função e os preços que substituem os de origem.</summary>
public sealed record AiUsageConfig(decimal? DefaultMonthlyAllowanceUsd, string? ChatModel, string? ReportModel, string? QuickModel, string? PricesJson);

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

    /// <summary>A franquia por omissão, os modelos e os preços escolhidos pelo produtor.</summary>
    public async Task<AiUsageConfig> UsageConfigAsync(CancellationToken cancellationToken = default)
    {
        var row = await context.AiGeneralSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return new AiUsageConfig(row?.DefaultMonthlyAllowanceUsd, row?.ChatModel, row?.ReportModel, row?.QuickModel, row?.PricesJson);
    }

    public async Task<AiUsageConfig> SaveUsageConfigAsync(AiUsageConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!string.IsNullOrWhiteSpace(config.PricesJson))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, decimal[]>>(config.PricesJson);
                if (parsed is null || parsed.Values.Any(pair => pair is not { Length: 2 } || pair[0] < 0 || pair[1] < 0))
                {
                    throw new ArgumentException("Os preços têm de ser {\"modelo\": [entrada, saída]}, em USD por milhão de tokens.");
                }
            }
            catch (System.Text.Json.JsonException)
            {
                throw new ArgumentException("Os preços não estão em JSON válido.");
            }
        }

        var row = await context.AiGeneralSettings.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = new AiGeneralSettings(Guid.NewGuid());
            context.AiGeneralSettings.Add(row);
        }

        row.ConfigureUsage(config.DefaultMonthlyAllowanceUsd, config.ChatModel, config.ReportModel, config.QuickModel, config.PricesJson);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        invalidator.Invalidate();
        return await UsageConfigAsync(cancellationToken).ConfigureAwait(false);
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
/// Lê da base de dados a chave e os modelos escolhidos pelo produtor, para toda a aplicação, e guarda-os em memória por pouco tempo
/// (20 s): uma alteração vale já neste posto e nos outros ao fim desse tempo. Se a base não responder, usa o último valor conhecido.
/// </summary>
public sealed class DatabaseAiKeySource(IServiceScopeFactory scopes, IAtSecretProtector protector, TimeProvider time) : IAiApiKeySource, IAiApiKeyInvalidator
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(20);

    private sealed record Snapshot(string? Key, string? Chat, string? Report, string? Quick);

    private readonly object gate = new();
    private Snapshot cached = new(null, null, null, null);
    private DateTimeOffset validUntil = DateTimeOffset.MinValue;

    public void Invalidate()
    {
        lock (gate)
        {
            validUntil = DateTimeOffset.MinValue;
        }
    }

    public string? CurrentKey() => Load().Key;

    public string? ModelOverride(AiFeature feature)
    {
        var snapshot = Load();
        return feature switch
        {
            AiFeature.Chat => snapshot.Chat,
            AiFeature.Report => snapshot.Report,
            _ => snapshot.Quick,
        };
    }

    private Snapshot Load()
    {
        lock (gate)
        {
            if (time.GetUtcNow() < validUntil)
            {
                return cached;
            }
        }

        var loaded = cached;
        try
        {
            using var scope = scopes.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var row = context.AiGeneralSettings.AsNoTracking().SingleOrDefault();
            loaded = row is null
                ? new Snapshot(null, null, null, null)
                : new Snapshot(string.IsNullOrEmpty(row.EncryptedApiKey) ? null : protector.Unprotect(row.EncryptedApiKey), row.ChatModel, row.ReportModel, row.QuickModel);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Sem base de dados (arranque, falha de rede): fica o último valor conhecido.
        }

        lock (gate)
        {
            cached = loaded;
            validUntil = time.GetUtcNow() + CacheFor;
            return cached;
        }
    }
}
