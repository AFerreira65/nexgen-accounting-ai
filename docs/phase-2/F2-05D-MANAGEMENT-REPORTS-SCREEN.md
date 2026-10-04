# F2-05D — Ecrã «Relatórios de gestão» na PWA

Quarto sub-incremento do [F2-05](F2-05-AI-MANAGEMENT-REPORTS.md). O ecrã que
liga o interruptor, gera e mostra os relatórios redigidos.

## Regras

- **`WorkContext`** ganha `managementReportCatalog`, `aiSettings`,
  `saveAiSettings`, `generateManagementReport(companyId, { kind? | question? },
  { from, to })`, `managementReports` e `managementReport(id)`. Tipos novos em
  `demo-data.ts` (`ManagementReportCatalogEntry`, `AiSettings`,
  `ManagementReportResult` e secções, `ManagementReportView`).
- **`/relatorios-gestao`** (`ManagementReportsPage`) — em modo demo/teste mostra
  o cabeçalho e «Requer ligação ao SQL Server»; ligado à base:
  - **Configuração** — interruptores `Enabled` e `AutoMonthly` (PUT
    `ai-settings`); quando o narrador não tem chave no servidor, uma nota e a
    geração fica indisponível; caso contrário mostra o modelo;
  - **Gerar** — tipo pré-definido (do catálogo) ou pergunta livre, e um período
    (arranca no exercício da empresa). «Gerar» só ativa com o interruptor
    ligado, chave presente e datas; chama `management-reports/generate`;
  - **Resultado** — o resumo, as secções (cada uma com as ligações à origem —
    `conta` → `/contas/{código}`, `movimento` → `/movimentos/{ref}`), os
    alertas, os indicadores, e o aviso fixo; «Imprimir / PDF» (folha
    `@media print` — só o resultado sai) e «Indicadores CSV»;
  - **Histórico** — os últimos 20, com «Abrir» para recarregar um.
- Entrada no menu lateral («Relatórios de gestão», ícone Sparkles) e no
  `screenTable.ts` (`reporting/ManagementReport`, para o menu «Opções»).

Sem alteração de API nem de esquema.

## Validação efetuada

- `NexGenAccountingAI.Web`: `App.test.tsx` — o ecrã `/relatorios-gestao` monta e, sem
  ligação à base, mostra «Requer ligação ao SQL Server». `tsc`, `eslint` e os
  setenta e nove testes de interface verdes.
- `dotnet build`/`dotnet test` na solução (cento e cinquenta e sete testes
  .NET); `scripts/build.ps1 -SyntheticOnly` e
  `scripts/verify-security.ps1` sem ocorrências.
- Verificação em direto contra `accountingAI` (Vidreira Algarvia): o ecrã
  carrega o catálogo, mostra o interruptor desligado e a nota «sem chave de API»
  (o servidor de desenvolvimento não tem chave), e o botão «Gerar» fica
  desativado — a degradação sem chave funciona.

## Fora deste incremento

- a geração mensal automática (`AutoMonthly`) no `Worker` — F2-05E;
- uma geração real contra a API do Claude (precisa de uma chave configurada).
