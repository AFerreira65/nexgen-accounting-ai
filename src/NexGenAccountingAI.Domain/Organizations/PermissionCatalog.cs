namespace NexGenAccountingAI.Domain.Organizations;

/// <summary>
/// Uma permissão do catálogo: um recurso (um ecrã ou área de trabalho) e uma ação sobre ele.
/// <see cref="AccountantOnly"/> marca as que dependem da senha do contabilista certificado (TOC):
/// só quem é «Contabilista» as tem, qualquer que seja o grupo (ADR-0018).
/// </summary>
public sealed record PermissionDefinition(
    string Key,
    string Resource,
    string ResourceLabel,
    string Section,
    string Action,
    string ActionLabel,
    bool AccountantOnly = false);

/// <summary>
/// O catálogo fixo de permissões, igual para todos os gabinetes. A chave é <c>recurso.ação</c>
/// (por exemplo <c>movements.create</c>). Os grupos de permissões guardam apenas chaves deste catálogo.
/// </summary>
public static class PermissionCatalog
{
    /// <summary>O pedido só exige um utilizador com acesso ativo à empresa (sem permissão específica).</summary>
    public const string AnyMember = "*";

    public const string View = "view";

    public static IReadOnlyList<PermissionDefinition> All { get; } = Build();

    private static readonly HashSet<string> Keys = All.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);

    public static bool Exists(string key) => Keys.Contains(key);

    public static bool IsAccountantOnly(string key) => All.Any(item => item.Key == key && item.AccountantOnly);

    /// <summary>As chaves que um utilizador que não é contabilista nunca pode ter.</summary>
    public static IReadOnlySet<string> AccountantOnlyKeys { get; } =
        All.Where(item => item.AccountantOnly).Select(item => item.Key).ToHashSet(StringComparer.Ordinal);

    public static IReadOnlySet<string> AllKeys => Keys;

    // Os grupos de origem de cada gabinete. Não se editam; copiam-se para criar grupos próprios.
    public const string ViewerSystemKey = "consulta";
    public const string EntrySystemKey = "lancamentos";
    public const string FullSystemKey = "completo";

    public static IReadOnlyList<(string SystemKey, string Name, string Description)> SystemGroups { get; } =
    [
        (ViewerSystemKey, "Consulta", "Só consulta e exporta; não altera nada."),
        (EntrySystemKey, "Lançamentos", "Consulta tudo e trabalha nos movimentos, bancos, arquivo e terceiros; não faz a parte fiscal."),
        (FullSystemKey, "Completo", "Tudo, exceto o que exige a senha do contabilista."),
    ];

    /// <summary>As permissões de um grupo de origem.</summary>
    public static IReadOnlyCollection<string> SystemGroupPermissions(string systemKey)
    {
        var views = All.Where(item => item.Action == View).Select(item => item.Key);
        switch (systemKey)
        {
            case ViewerSystemKey:
                return views.Concat(["reports.export", "ai.use", "saftexport.export"]).Distinct().ToList();
            case EntrySystemKey:
                return views.Concat(
                [
                    "reports.export", "ai.use", "saftexport.export",
                    "movements.create", "movements.edit", "movements.review",
                    "suggested.post", "banks.import", "banks.reconcile",
                    "archive.create", "archive.edit", "tasks.edit",
                    "parties.create", "parties.edit", "efatura.import",
                ]).Distinct().ToList();
            case FullSystemKey:
                return All.Where(item => !item.AccountantOnly).Select(item => item.Key).ToList();
            default:
                return [];
        }
    }

    private static List<PermissionDefinition> Build()
    {
        var list = new List<PermissionDefinition>();

        void Resource(string section, string key, string label, params (string Action, string Label, bool AccountantOnly)[] actions)
        {
            foreach (var (action, actionLabel, accountantOnly) in actions)
            {
                list.Add(new PermissionDefinition($"{key}.{action}", key, label, section, action, actionLabel, accountantOnly));
            }
        }

        (string, string, bool) V() => ("view", "Consultar", false);
        (string, string, bool) A(string action, string label) => (action, label, false);
        (string, string, bool) T(string action, string label) => (action, label, true);

        Resource("Trabalho", "movements", "Movimentos", V(), A("create", "Lançar"), A("edit", "Alterar"), A("review", "Conferir"), A("void", "Anular"), A("reopen", "Reabrir"));
        Resource("Trabalho", "suggested", "Lançamentos sugeridos", V(), A("post", "Lançar e dispensar"));
        Resource("Trabalho", "banks", "Bancos", V(), A("import", "Importar extratos"), A("reconcile", "Reconciliar"), A("post", "Lançar movimentos"));
        Resource("Trabalho", "archive", "Arquivo digital", V(), A("create", "Juntar"), A("edit", "Alterar"), A("delete", "Apagar"));
        Resource("Trabalho", "tasks", "Tarefas", V(), A("edit", "Criar e alterar"));

        Resource("Mapas", "reports", "Mapas e demonstrações", V(), A("export", "Exportar e imprimir"));
        Resource("Mapas", "custom", "Mapas e listagens à medida", V(), A("edit", "Criar e alterar"));

        Resource("Fiscal", "obligations", "Calendário de obrigações", V(), A("edit", "Registar entregas"));
        Resource("Fiscal", "vat", "IVA", V(), A("edit", "Preparar a declaração"), A("post", "Lançar o apuramento"));
        Resource("Fiscal", "vatconfig", "Configuração e códigos de IVA", V(), A("edit", "Alterar"));
        Resource("Fiscal", "irc", "IRC, Modelo 22 e IES", V(), A("edit", "Preparar"));
        Resource("Fiscal", "withholdings", "Retenções na fonte", V());
        Resource("Fiscal", "dossier", "Dossier fiscal", V(), A("edit", "Alterar"));
        Resource("Fiscal", "at", "Comunicação com a AT", V(),
            A("send_vat", "Enviar a declaração de IVA"),
            T("send_ies", "Enviar a IES"),
            T("send_modelo22", "Enviar o Modelo 22"),
            A("receive_efatura", "Receber as compras do e-Fatura"),
            T("credentials", "Ver e alterar as senhas da AT"));

        Resource("Gestão", "management", "Análises e indicadores", V(), A("edit", "Gravar análises e dispensar avisos"));
        Resource("Gestão", "budget", "Orçamento", V(), A("edit", "Alterar"));
        Resource("Gestão", "ai", "Assistente e IA", A("use", "Usar"), A("settings", "Configurar"));
        Resource("Gestão", "closing", "Fecho do mês", V(), A("edit", "Alterar"));

        Resource("Exercício", "yearend", "Apuramento e abertura do exercício", V(), A("execute", "Executar"), A("reopen", "Reabrir"));

        Resource("Cadastros", "accounts", "Plano de contas", V(), A("create", "Abrir contas"), A("edit", "Alterar"));
        Resource("Cadastros", "journals", "Diários", A("create", "Criar"), A("edit", "Alterar"));
        Resource("Cadastros", "documents", "Documentos", A("create", "Criar"), A("edit", "Alterar"));
        Resource("Cadastros", "series", "Séries", V(), A("edit", "Alterar"));
        Resource("Cadastros", "parties", "Terceiros", V(), A("create", "Criar"), A("edit", "Alterar"));
        Resource("Cadastros", "costcenters", "Centros de custo", V(), A("create", "Criar"), A("edit", "Alterar"));
        Resource("Cadastros", "fixedassets", "Ativos fixos", V(), A("create", "Criar"), A("edit", "Alterar"), A("post", "Lançar depreciações e alienações"));
        Resource("Cadastros", "templates", "Lançamentos-tipo", V(), A("edit", "Alterar"));

        Resource("Dados", "portal", "Portal do cliente", V(), A("publish", "Convidar clientes e publicar"));
        Resource("Dados", "saftimport", "Importar SAF-T", A("import", "Importar"));
        Resource("Dados", "saftexport", "Exportar SAF-T", A("export", "Exportar"));
        Resource("Dados", "efatura", "Compras e-Fatura", V(), A("import", "Importar ficheiro"));

        return list;
    }
}
