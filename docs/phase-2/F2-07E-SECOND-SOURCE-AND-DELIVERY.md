# F2-07E — Segunda origem e fronteira do envio

Quinto e último sub-incremento do [F2-07](F2-07-CONFIGURABLE-REPORTS.md).
**Fecha o épico.** Acrescenta a origem `SaldosDeConta` ao motor de análises e
documenta a fronteira do envio por email.

## Regras — origem `SaldosDeConta`

- `AnalysisFieldCatalog` ganha a origem `SaldosDeConta` com os campos: conta,
  nome da conta, **classe** (o primeiro dígito do código), saldo de abertura,
  débito, crédito e **saldo**. Débito/crédito/saldos são agregáveis;
  conta/nome/classe são agrupáveis.
- `AnalysisRunner` reaproveita o balancete
  (`AccountLedgerService.TrialBalanceAsync`, já filtrado por empresa, período e
  sem anulados) para produzir uma linha por conta de movimento; o resto do
  pipeline — filtros, ordenação, agrupamentos com subtotais em todos os níveis,
  total geral, limite de 5000 — é o mesmo dos movimentos. O `RunAsync` passou a
  ramificar por origem; a validação e o acesso à empresa são comuns.
- `analyses/catalog` passa a listar as duas origens; o ecrã «Análises» ganha um
  selector de **Origem** (mudar de origem limpa a definição, porque os campos
  diferem).

Sem alteração de esquema nem de API (o endpoint `analyses/run` já aceitava
qualquer origem do catálogo).

## Fronteira — envio de relatórios por email

O ponto 7 da visão pede enviar mapas e relatórios por email. Isso **fica fora
do âmbito local da Fase 2** e será um épico próprio quando houver
infraestrutura de correio:

- precisa de **configuração SMTP por gabinete** (servidor, credenciais,
  remetente) — segredos que a plataforma local não gere;
- precisa de uma **fila/worker** para o envio assíncrono, tratamento de
  devoluções e reenvio;
- precisa de decidir o **formato anexado** (PDF gerado no servidor vs. o PDF do
  navegador) e a política de retenção.

O que já existe cobre a necessidade imediata: **impressão → «Guardar como PDF»
do navegador** (relatórios fixos e análises, com o cabeçalho configurável de
[F2-07D](F2-07D-REPORT-TEMPLATES.md)) e **exportação CSV**. Um passo intermédio
possível, sem SMTP, é um botão «Enviar por email» que abre o cliente de correio
do sistema (`mailto:`) com o assunto preenchido, deixando o utilizador anexar o
ficheiro exportado.

## Validação efetuada

- `NexGenAccountingAI.IntegrationTests`: `AnalysisRunnerTests` acrescenta o cenário
  `SaldosDeConta` — três contas de movimento, agrupar por classe, somar o
  saldo: classe 6 = 390, classe 2 = −390, total geral 0.
- `NexGenAccountingAI.Web`: lint, build e setenta e oito testes de interface (sem
  novos — o selector de origem é uma adição ao construtor já testado);
- `dotnet build`/`dotnet test` na solução inteira (cento e quarenta e oito
  testes .NET); `scripts/build.ps1 -SyntheticOnly` e
  `scripts/verify-security.ps1` sem ocorrências;
- verificação em direto contra `accountingAI` (empresa Vidreira Algarvia,
  exercício de 2025): a origem `SaldosDeConta` agrupada por classe devolve
  1 854 contas e os subtotais por classe (1 = 5,66 M€, 6 = 7,20 M€,
  7 = −18,65 M€, …) com total geral de saldo **0,00** (o balancete fecha); o
  ecrã «Análises» troca as colunas ao mudar de origem.

## Épico F2-07 concluído

F2-07A (definições guardadas + catálogo) · F2-07B (runner dos movimentos) ·
F2-07C (ecrã «Análises») · F2-07D (modelos de impressão dos relatórios fixos) ·
F2-07E (origem dos saldos de conta + fronteira do envio). Fica em aberto, para
épicos futuros: mais origens (contas correntes, compras e-Fatura, resultado do
cruzamento), reordenar colunas com o rato, partilha de análises ao nível do
gabinete, e o envio por email com SMTP.
