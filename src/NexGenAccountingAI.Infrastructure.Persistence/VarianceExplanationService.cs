using System.Text.Json;
using NexGenAccountingAI.Application.Analysis;
using NexGenAccountingAI.Domain.Reporting;
using NexGenAccountingAI.Infrastructure.Persistence.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NexGenAccountingAI.Infrastructure.Persistence;

/// <summary>A recusa (IA desligada ou sem chave) ou a explicação.</summary>
public sealed record VarianceExplanationOutcome(string? Refusal, VarianceExplanation? Explanation);

/// <summary>
/// Pede à IA um texto sobre a comparação entre períodos (F4F-03). Exige acesso à empresa, o interruptor da IA
/// ligado nessa empresa e uma chave configurada. À IA só vão os totais e as maiores variações já calculadas
/// (com os nomes das contas e dos terceiros que mais pesam), nunca os lançamentos.
/// </summary>
public sealed class VarianceExplanationService(
    AccountingDbContext context, VarianceService variance, IVarianceExplainer explainer, AiGovernor? governor = null)
{
    /// <summary>Quantas contas seguem para a IA.</summary>
    public const int RowsForTheModel = 15;

    // Os acentos seguem como texto e não escapados: o modelo lê melhor e gasta menos.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <returns><c>null</c> sem acesso à empresa.</returns>
    public async Task<VarianceExplanationOutcome?> ExplainAsync(
        Guid userId, Guid companyId, DateOnly from, DateOnly to, DateOnly compareFrom, DateOnly compareTo, CancellationToken cancellationToken = default)
    {
        var report = await variance.BuildAsync(userId, companyId, from, to, compareFrom, compareTo, cancellationToken).ConfigureAwait(false);
        if (report is null)
        {
            return null;
        }

        var enabled = await context.CompanyAiSettings.AsNoTracking().AnyAsync(item => item.CompanyId == companyId && item.Enabled, cancellationToken).ConfigureAwait(false);
        if (!enabled)
        {
            return new VarianceExplanationOutcome("A IA está desligada para esta empresa. Ligue-a nas definições da empresa para pedir explicações.", null);
        }

        if (!explainer.IsConfigured)
        {
            return new VarianceExplanationOutcome("A IA não está configurada neste servidor (falta a chave da Anthropic).", null);
        }

        if (report.Rows.Count == 0)
        {
            return new VarianceExplanationOutcome("Não há variações entre os dois períodos para explicar.", null);
        }

        if (governor is not null && await governor.RefusalAsync(companyId, cancellationToken).ConfigureAwait(false) is { } limit)
        {
            return new VarianceExplanationOutcome(limit, null);
        }

        var payload = new
        {
            periodoAtual = new { de = report.From, ate = report.To },
            periodoAnterior = new { de = report.CompareFrom, ate = report.CompareTo },
            totais = new
            {
                rendimentos = report.Totals.Income, rendimentosAnteriores = report.Totals.PreviousIncome,
                gastos = report.Totals.Expenses, gastosAnteriores = report.Totals.PreviousExpenses,
                resultado = report.Totals.Result, resultadoAnterior = report.Totals.PreviousResult,
            },
            contas = report.Rows.Take(RowsForTheModel).Select(row => new
            {
                conta = row.AccountCode, nome = row.AccountName, tipo = row.Kind == "income" ? "rendimento" : "gasto",
                atual = row.Current, anterior = row.Previous, diferenca = row.Delta, percentagem = row.Percent, favoravel = row.Favorable,
                terceiros = row.TopParties.Select(party => new { nome = party.Name, atual = party.Current, anterior = party.Previous }),
            }),
        };
        // Os nomes dos terceiros (e o da empresa) vão trocados por marcadores; a explicação volta com os verdadeiros.
        var protector = governor is null ? null : (await governor.PrivacyAsync(companyId, cancellationToken).ConfigureAwait(false)).Protector;
        var json = JsonSerializer.Serialize(payload, Json);
        var explanation = await explainer.ExplainAsync(protector is null ? json : protector.Protect(json), cancellationToken).ConfigureAwait(false);
        if (protector is not null)
        {
            explanation = explanation with { Text = protector.Restore(explanation.Text) };
        }

        if (governor is not null)
        {
            await governor.RecordAsync(companyId, userId, AiFeature.Variance, explanation.Model, explanation.PromptTokens, explanation.OutputTokens, cancellationToken).ConfigureAwait(false);
        }

        return new VarianceExplanationOutcome(null, explanation);
    }
}
