using AccountingAI.Application.ManagementReports;

namespace AccountingAI.Infrastructure.Ai;

/// <summary>
/// Os enunciados (pt-PT, em código) do narrador. O de sistema fixa as regras e a
/// forma do JSON de resposta; o do utilizador leva o dossiê e, se for o caso, a
/// pergunta livre.
/// </summary>
public static class ManagementReportPrompts
{
    private const string BaseSystem =
        """
        És um assistente de contabilidade que redige relatórios de gestão em português europeu (pt-PT).
        Trabalhas apenas a partir do dossiê de dados fornecido — um retrato já reconciliado da empresa
        no período. Regras:
        - Não inventes valores. Se algo não está no dossiê, di-lo explicitamente.
        - Cita a origem: sempre que referes um valor de uma conta ou de um movimento, acrescenta-o às
          "referencias" da secção (tipo "conta" com o código da conta, ou tipo "movimento" com a
          referência do lançamento).
        - Quando o dossiê indica que não há período homólogo, não faças comparações homólogas.
        - Sê objetivo e conciso. Valores em euros, com duas casas decimais.
        Respondes SÓ com um objeto JSON válido, sem texto nem cercas antes ou depois, com esta forma:
        {
          "resumo": "string",
          "seccoes": [ { "titulo": "string", "corpo": "string",
                         "referencias": [ { "tipo": "conta|movimento", "codigo": "string" } ] } ],
          "alertas": [ "string" ],
          "indicadores": [ { "nome": "string", "valor": "string", "comentario": "string ou null" } ]
        }
        """;

    private static readonly IReadOnlyDictionary<ManagementReportKind, string> TaskByKind =
        new Dictionary<ManagementReportKind, string>
        {
            [ManagementReportKind.SinteseMensal] =
                "Redige uma síntese mensal: o resultado do período e a variação homóloga, os principais " +
                "desvios de rendimentos e de gastos, a evolução dos rácios, e alertas do que merece atenção.",
            [ManagementReportKind.AnaliseRentabilidade] =
                "Analisa a rentabilidade: margem líquida, peso dos FSE e dos gastos com pessoal, e a sua " +
                "evolução homóloga. Explica o que puxa a margem para cima ou para baixo.",
            [ManagementReportKind.PosicaoFinanceira] =
                "Descreve a posição financeira no fim do período: ativo, passivo e capital próprio, liquidez " +
                "geral, fundo de maneio e autonomia financeira. Comenta a solidez.",
            [ManagementReportKind.RevisaoIva] =
                "Revê o IVA do período com base no cruzamento com o e-Fatura: divergências, faturas por lançar, " +
                "e o que confirmar antes da declaração periódica.",
            [ManagementReportKind.AlertasDeFecho] =
                "Lista os pontos a rever antes do fecho: contas com saldos anómalos, movimentos de valor " +
                "invulgar, divergências do cruzamento, rácios fora do esperado. Ordena por prioridade.",
        };

    /// <summary>O enunciado de sistema para um pedido (por tipo, ou genérico para pergunta livre).</summary>
    public static string System(ManagementReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var task = request.Kind is { } kind && TaskByKind.TryGetValue(kind, out var text)
            ? text
            : "Responde à pergunta do utilizador estritamente a partir do dossiê. Se a resposta não está nos dados, di-lo.";
        return $"{BaseSystem}\n\nTarefa: {task}";
    }

    /// <summary>A mensagem do utilizador: a pergunta livre (se houver) e o dossiê em JSON.</summary>
    public static string User(ManagementReportRequest request, string dossierJson)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ask = request.IsQuestion
            ? $"Pergunta do utilizador: {request.Question!.Trim()}\n\n"
            : string.Empty;
        return $"{ask}Dossiê de dados (JSON):\n{dossierJson}";
    }
}
