# F3-06 — Exportação do SAF-T (PT) de contabilidade

Primeiro épico do Bloco B da [Fase 3](README.md). A aplicação lia e validava
SAF-T mas não produzia nenhum. A capacidade de exportar o ficheiro de
contabilidade é exigida a quem tem contabilidade organizada (é o que a
inspeção pede), mesmo com a entrega para a IES adiada.

## Âmbito

Ficheiro de **contabilidade** (`TaxAccountingBasis = C`), versão **1.04_01**
(Portaria n.º 302/2016), por exercício, em Windows-1252. Sem documentos de
faturação: a aplicação não emite.

## Sub-incrementos

### F3-06A — Exportador, análise e ecrã (feito)

- **`Application/Saft/SaftExport`** — o modelo do ficheiro
  (`SaftExportModel`), o relatório (`SaftExportReport`, com problemas
  bloqueantes ou não) e a fronteira `ISaftAccountingWriter`. `SaftProducer`
  identifica o programa no cabeçalho: NIF do produtor `518104486`,
  `SoftwareCertificateNumber` zero (um programa de contabilidade não é
  certificado) e `ProductID` «Accounting AI/RumaoAlgarismo - Unipessoal Lda».
- **`Infrastructure.Files/SaftAccountingWriter`** — escreve o XML em
  streaming, pela ordem do esquema. As moradas que a aplicação não conhece
  saem como «Desconhecido», que o formato admite.
- **`Infrastructure.SqlServer/SaftExportService`** — monta o modelo de um
  exercício:

| Parte | Regra |
|---|---|
| Lançamentos | Não anulados, com data no exercício. |
| Abertura | Os lançamentos de abertura **não** saem como movimentos: dão os saldos de abertura das contas. |
| Contas | As de movimento com saldo ou movimento, e as agregadoras de que dependem. As de um dígito ficam de fora (o formato pede dois caracteres), pelo que as de dois dígitos são o 1.º grau (`GR`). |
| Saldos | Devedor e credor em separado; as agregadoras somam os saldos devedores e credores das suas contas. |
| `GroupingCategory` | `GR` 1.º grau, `GA` agregadora, `GM` movimento — só as `GM` levam `TaxonomyCode`. |
| `TransactionID` | «data diário número», sem espaços em cada parte. Uma referência que já tem essa forma (lançamento importado de um SAF-T) sai tal como veio. |
| `TransactionType` | `A` no apuramento de resultados; `N` nos restantes. |
| `Period` | O mês do lançamento. |
| Terceiro | O da primeira linha que o indica; a conta dessa linha (21 ou 22) diz se é cliente ou fornecedor. O formato admite um ou outro, não os dois. |
| `SourceID` | Quem criou o lançamento; «Sistema» quando não se sabe. |

- **Problemas bloqueantes** (o ficheiro não é gerado): NIF da empresa
  inválido; conta de movimento com saldo sem código de taxonomia; conta de
  movimento sem agregadora; identificadores de transação repetidos.
- **Avisos**: morada da empresa por registar; exercício sem lançamento de
  abertura havendo movimentos anteriores (saldos de abertura a zero).
- **API:** `GET .../saft/export-report?year=` (só analisa) e
  `GET .../saft/export?year=` — o ficheiro, ou `409` com o relatório quando há
  problemas bloqueantes.
- **PWA:** ecrã **«Exportar SAF-T»** (`/exportar-saft`, grupo «Dados») —
  «Analisar o exercício» mostra as contagens e os problemas; «Gerar o
  ficheiro» só fica disponível sem problemas bloqueantes.

**Validação efetuada**

- **Contra o XSD oficial da AT** (`SAFTPT1.04_01.xsd`, XSD 1.1 com as regras
  de `GroupingCategory` e taxonomia): um ficheiro gerado pelo escritor, com
  compras, vendas, apuramento, cliente e fornecedor estrangeiro, validou com
  **zero erros**. Controlo negativo: retirando o `TaxonomyCode` de uma conta
  `GM`, pondo um espaço a mais no `TransactionID` ou cliente e fornecedor na
  mesma transação, o validador acusa um erro em cada caso. Feito fora do
  repositório — o XSD continua a não ser redistribuído (ver
  `Infrastructure.Files/Schemas/README.md`).
- `AccountingAI.Infrastructure.Files.Tests`: `SaftAccountingWriterTests` —
  codificação, ordem dos blocos, cabeçalho, categorias e taxonomia, totais,
  linhas; e o ficheiro é **relido pelos leitores da importação** (contas,
  movimentos e terceiros).
- `AccountingAI.IntegrationTests`: `SaftExportServiceTests` — abertura como
  saldos, agregadoras a somar, conta de um dígito e conta sem movimento de
  fora, referência importada preservada, lançamento anulado e de outro
  exercício excluídos, terceiro como cliente; com uma conta sem taxonomia e
  sem agregadora o ficheiro não é escrito.
- `AccountingAI.Api.Tests`: `SaftExportReportsAndRefusesAnInvalidCompany`.
- `AccountingAI.Web`: `tsc`, `eslint`, os 91 testes; o ecrã foi aberto no
  navegador em modo de demonstração.
- A suite completa: 231 testes .NET.

**Por verificar**

- A exportação de uma **empresa real** e a validação desse ficheiro no
  validador da AT — o XSD confirma a estrutura, não as regras de negócio.
- O ficheiro é montado em memória; numa empresa com dezenas de milhares de
  lançamentos convém medir o tempo e a memória.

**Pressupostos a confirmar com um contabilista**

- `Period` = mês e `TransactionType` = `A` só no apuramento; não se usam os
  períodos 13 a 16 nem os tipos `R` e `J`.
- Os saldos das agregadoras como soma separada de devedores e credores.

### F3-06B — Dados fiscais da empresa (a seguir)

Morada, código postal e localidade da empresa, para o cabeçalho deixar de
sair com «Desconhecido»; e a morada dos terceiros.
