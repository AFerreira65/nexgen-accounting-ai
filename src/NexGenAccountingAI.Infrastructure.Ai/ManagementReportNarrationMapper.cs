using System.Text.Json;
using NexGenAccountingAI.Application.ManagementReports;

namespace NexGenAccountingAI.Infrastructure.Ai;

/// <summary>
/// Mapeia a resposta em texto do modelo (que deve ser um objeto JSON) para um
/// <see cref="ManagementReportResult"/>. Tolerante: se o modelo envolver o JSON
/// em cercas ou texto, fica-se com o primeiro objeto; se nada for legível,
/// devolve um resultado com um alerta em vez de rebentar.
/// </summary>
public static class ManagementReportNarrationMapper
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static ManagementReportResult FromJson(string content)
    {
        var payload = TryParse(content);
        if (payload is null)
        {
            return new ManagementReportResult(
                "Não foi possível interpretar a resposta do modelo.",
                [],
                ["A resposta da IA não veio em JSON válido. Tente gerar novamente."],
                [],
                ManagementReportResult.Notice);
        }

        var sections = (payload.Seccoes ?? [])
            .Select(section => new ManagementReportSection(
                section.Titulo ?? string.Empty,
                section.Corpo ?? string.Empty,
                (section.Referencias ?? [])
                    .Where(reference => !string.IsNullOrWhiteSpace(reference.Codigo))
                    .Select(reference => new ManagementReportReference(
                        NormalizeType(reference.Tipo), reference.Codigo!.Trim()))
                    .ToList()))
            .ToList();

        var indicators = (payload.Indicadores ?? [])
            .Select(indicator => new ManagementReportIndicator(
                indicator.Nome ?? string.Empty,
                indicator.Valor ?? string.Empty,
                string.IsNullOrWhiteSpace(indicator.Comentario) ? null : indicator.Comentario))
            .ToList();

        return new ManagementReportResult(
            payload.Resumo ?? string.Empty,
            sections,
            (payload.Alertas ?? []).Where(alert => !string.IsNullOrWhiteSpace(alert)).ToList(),
            indicators,
            ManagementReportResult.Notice);
    }

    private static Payload? TryParse(string content)
    {
        var json = ExtractObject(content);
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Payload>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractObject(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : null;
    }

    private static string NormalizeType(string? tipo) => tipo?.Trim().ToLowerInvariant() switch
    {
        "movimento" or "lancamento" or "lançamento" => "movimento",
        _ => "conta",
    };

    private sealed class Payload
    {
        public string? Resumo { get; set; }
        public List<SectionPayload>? Seccoes { get; set; }
        public List<string>? Alertas { get; set; }
        public List<IndicatorPayload>? Indicadores { get; set; }
    }

    private sealed class SectionPayload
    {
        public string? Titulo { get; set; }
        public string? Corpo { get; set; }
        public List<ReferencePayload>? Referencias { get; set; }
    }

    private sealed class ReferencePayload
    {
        public string? Tipo { get; set; }
        public string? Codigo { get; set; }
    }

    private sealed class IndicatorPayload
    {
        public string? Nome { get; set; }
        public string? Valor { get; set; }
        public string? Comentario { get; set; }
    }
}
