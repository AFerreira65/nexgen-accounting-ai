# F2-01 — Validação XSD e analítica do SAF-T (base para SVAT)

## Contexto

O que existe hoje na nova plataforma:

- `SaftHeaderReader`, `SaftAccountsReader` e `SaftEntriesReader`
  (`Infrastructure.Files`) leem o SAF-T em streaming com `XmlReader`, DTD
  proibido e `XmlResolver` nulo. **Não há validação de esquema.** Os leitores
  extraem campos pelo nome do elemento e lançam `SaftFileFormatException`
  quando um campo obrigatório falta ou não converte.
- A importação de movimentos reconcilia, no fim, os débitos e créditos do
  período do ficheiro com os totais declarados (`SaftAccountsReconciliation`,
  `SaftEntriesSummary`).
- O monitor WinForms (`AccountingAI.Core/Parsing/SaftParser.cs`) faz uma
  verificação «estrutural e analítica» leve — reconhece as versões `1.03_01`
  e `1.04_01`, valida `TaxAccountingBasis` contra `{C,E,F,I,P,R,S,T}`, avisa
  se o cabeçalho indica contabilidade sem diários — e diz explicitamente que
  «não substitui a validação oficial XSD/SVAT». Essa lógica **não foi
  portada** para a nova plataforma.

A lacuna: um SAF-T pode ser lido com sucesso pelos leitores atuais e ainda
assim não conformar com o XSD oficial (ordem de elementos, cardinalidades,
padrões, enumerações, tipos), ou conformar com o XSD e falhar regras de
negócio (NIF sem dígito de controlo válido, `NumberOfEntries` diferente da
contagem real, totais que não fecham, datas fora do período do cabeçalho,
referências a contas inexistentes). Hoje nada disto é reportado ao
utilizador de forma estruturada antes da importação.

## Objetivo

Um ficheiro SAF-T submetido passa por um **relatório de validação em
camadas**, disponível como passo próprio no ecrã de importação e como
endpoint, antes de qualquer escrita:

1. **XSD (estrutural)** — conformidade com o esquema oficial da AT para a
   versão declarada. Resultado: conforme / não conforme + lista de violações
   com severidade, linha, posição, caminho do elemento e mensagem.
2. **Analítica (regras de negócio)** — verificações que o XSD não cobre,
   compostas por regras independentes com severidade própria.
3. **Orientada a SVAT** — o subconjunto dos critérios do Selo de Validação AT
   que já é aferível sobre um ficheiro lido; o resto fica dependente de um
   futuro módulo que **produza** o SAF-T de contabilidade (fora desta fase,
   ver [`../CERTIFICACAO_AT.md`](../CERTIFICACAO_AT.md)).

O relatório é sempre apresentado como apoio; não afirma validação pela AT.

## Camadas de validação

### 1. XSD estrutural

- Esquemas suportados, embebidos como recursos no assembly
  `Infrastructure.Files`:
  - `urn:OECD:StandardAuditFile-Tax:PT_1.04_01` (Portaria 302/2016) — alvo
    principal do SAF-T de contabilidade;
  - `urn:OECD:StandardAuditFile-Tax:PT_1.03_01` (anterior) — aceite com
    aviso, porque ainda aparece em ficheiros reais.
- Validação **em streaming**: `XmlReaderSettings` com
  `ValidationType.Schema`, `XmlSchemaSet` pré-carregado e um
  `ValidationEventHandler` que acumula ocorrências sem abortar à primeira.
  Um SAF-T de contabilidade real chega às dezenas de MB, por isso o ficheiro
  não é materializado.
- Sem acesso a rede: `XmlResolver` nulo; `schemaLocation` do ficheiro é
  ignorado; a versão é decidida pelo `AuditFileVersion` do cabeçalho (lido
  primeiro) e, em falta ou desacordo, pelo namespace do elemento raiz.
- Limite de ocorrências reportadas (ex.: 200) com resumo do excedente, à
  semelhança do que F1-89 fez para as transações saltadas.

### 2. Analítica

Regras independentes, cada uma com identificador, severidade
(`Error`/`Warning`/`Information`), âmbito (`Header`/`MasterFiles`/
`GeneralLedgerEntries`/`Ficheiro`) e mensagem. Conjunto inicial:

| Id | Severidade | Verificação |
|----|-----------|-------------|
| `NIF-CHECKDIGIT` | Error | `TaxRegistrationNumber` (e NIFs de cliente/fornecedor) com dígito de controlo mod-11 válido |
| `BASIS-KNOWN` | Error | `TaxAccountingBasis` ∈ `{C,E,F,I,P,R,S,T}` |
| `PERIOD-RANGE` | Error | `StartDate ≤ EndDate`; `FiscalYear` coerente com o período |
| `ENTRY-DATES-IN-PERIOD` | Warning | cada `TransactionDate`/`GLPostingDate` dentro de `[StartDate, EndDate]` |
| `ENTRY-COUNT` | Error | `GeneralLedgerEntries/NumberOfEntries` = nº de `Transaction` lidos |
| `FILE-TOTALS` | Error | `TotalDebit`/`TotalCredit` do bloco = soma das linhas; `TotalDebit = TotalCredit` |
| `TRANSACTION-BALANCED` | Error | cada `Transaction` com `Σ DebitAmount = Σ CreditAmount` |
| `ACCOUNT-REF-RESOLVES` | Error | todo o `AccountID` referido nas linhas existe em `MasterFiles/GeneralLedgerAccounts` |
| `ACCOUNTING-HAS-JOURNALS` | Warning | base `C`/`I` sem `Journal` no ficheiro |
| `OPENING-CLOSING-CONSISTENT` | Warning | contas com `OpeningDebitBalance`/`OpeningCreditBalance` e `ClosingDebitBalance`/`ClosingCreditBalance` coerentes com o movimento do período |
| `PRODUCT-COMPANY-TIN` | Information | `ProductCompanyTaxID`/`ProductID`/`SoftwareCertificateNumber` presentes (informativo enquanto a plataforma não é programa certificado) |

As regras não repetem o que o XSD já garante; assumem um ficheiro que já
passou (ou não) a camada 1 e correm sempre, para o relatório ser completo
mesmo quando o XSD falha.

### 3. Orientada a SVAT

Um documento-inventário (`F2-01F`) que mapeia os critérios publicados do
SVAT para: (a) já verificado pelas camadas 1–2, (b) verificável mas ainda
não implementado, (c) dependente de a plataforma **produzir** o SAF-T de
contabilidade. Não há aqui código de produção nesta fase — é o registo do
que falta e a base do pedido de SVAT quando o módulo de exportação existir.

## Desenho

### Application

```
Application/Saft/
  ISaftSchemaValidator.cs      // Task<SaftSchemaValidationResult> ValidateAsync(Stream, CancellationToken)
  SaftValidationReport.cs      // record: Version, SchemaValid, IReadOnlyList<SaftValidationIssue>, Summary
  SaftValidationIssue.cs       // record: RuleId?, Severity, Scope, Line, Position, Path, Message
  ISaftAnalyticalValidator.cs  // opera sobre os modelos já lidos + contagens
  Rules/                       // uma classe por regra, todas ISaftAnalyticalRule
```

`SaftValidationSeverity { Information, Warning, Error }` no `Domain`
(`Domain/Saft/`), reutilizável.

### Infrastructure.Files

- `SaftSchemaValidator : ISaftSchemaValidator` — carrega o `XmlSchemaSet` uma
  vez (estático, imutável), valida em streaming, devolve as ocorrências.
- XSDs em `Infrastructure.Files/Schemas/SAFTPT_1.04_01.xsd` e
  `SAFTPT_1.03_01.xsd`, marcados como `EmbeddedResource`.

### API

- `POST /api/v1/development/users/{userId}/saft/validate` — corpo = XML
  (`application/xml`), como os restantes endpoints SAF-T. Levanta o limite de
  corpo como o `saft/inspect`. Resposta:

  ```json
  {
    "version": "1.04_01",
    "schemaValid": false,
    "counts": { "error": 3, "warning": 5, "information": 1 },
    "issues": [
      { "ruleId": null, "severity": "Error", "scope": "GeneralLedgerEntries",
        "line": 10432, "position": 17, "path": "AuditFile/GeneralLedgerEntries/Journal[3]/Transaction[88]/Lines/DebitLine[2]/DebitAmount",
        "message": "O elemento 'DebitAmount' tem um valor que não respeita o tipo 'SAFmonetaryType'." },
      { "ruleId": "ENTRY-COUNT", "severity": "Error", "scope": "GeneralLedgerEntries",
        "message": "NumberOfEntries declara 12 041; foram lidos 12 038." }
    ],
    "truncated": false
  }
  ```

- `saft/inspect` ganha um campo `schemaValid` (bool) e `issueCounts` a partir
  de uma passagem rápida só à camada XSD, para o ecrã dar sinal cedo sem o
  utilizador pedir a validação completa.

### PWA

- No ecrã de importação (`SaftImportPage`), antes dos painéis de contas e
  movimentos, um passo **«Validar ficheiro»** que chama `saft/validate` e
  mostra o relatório agrupado por severidade e âmbito, com o caminho do
  elemento e a linha, e exportação CSV do relatório (isolada em
  `src/app/csv.ts`, sem endpoint próprio).
- Enquanto houver ocorrências `Error`, os botões de importar contas/movimentos
  ficam desativados, com uma opção explícita **«Importar mesmo assim»** que
  regista a decisão (o utilizador é o contabilista responsável). `Warning` e
  `Information` não bloqueiam.
- Rótulos pt-PT para `severity` e `scope` num módulo partilhado, como
  `entrySources`/`accountClasses`.

### Persistência (F2-01E)

- Tabela `accounting.SaftValidationReport` — `Id`, `CompanyId?` (nulo quando
  a validação corre antes de haver empresa), `UserId`, `FileName`,
  `AuditFileVersion`, `SchemaValid`, `ErrorCount`/`WarningCount`,
  `CreatedAtUtc`, e as ocorrências em JSON ou numa tabela-filha
  `SaftValidationReportIssue`.
- Endpoint de histórico por empresa e ligação a partir do ecrã da empresa.
- Migração EF Core versionada (ADR-0002); testes de escrita na suíte de
  integração, não na de API.

## Sub-incrementos

### F2-01A — Validador XSD em streaming

- **Regras:** XSDs 1.04_01 e 1.03_01 embebidos; `ISaftSchemaValidator` +
  `SaftSchemaValidator`; `SaftValidationReport`/`SaftValidationIssue`;
  `SaftValidationSeverity` no `Domain`. Seleção de versão pelo cabeçalho e
  pelo namespace. Limite de ocorrências com resumo do excedente.
- **Validação prevista:** testes em `Infrastructure.Files.Tests` com um
  ficheiro conforme (0 ocorrências), um com erro estrutural conhecido
  (elemento fora de ordem, valor fora do tipo), e um `1.03_01` (aceite com
  aviso). Confirmar que a validação não materializa o ficheiro (medição de
  memória num ficheiro grande dos exemplos).
- **Fora:** endpoint, regras analíticas, PWA.

### F2-01B — Endpoint `saft/validate`

- **Regras:** `POST .../saft/validate` devolve o `SaftValidationReport` só
  com a camada XSD; `saft/inspect` ganha `schemaValid`/`issueCounts`. Fase
  do `/foundation` passa a `F2-01`.
- **Validação prevista:** testes de API de leitura com um ficheiro conforme e
  um não conforme dos exemplos; `4xx` só para XML ilegível, não para XML
  válido mas não conforme (esse é `200` com `schemaValid:false`).
- **Fora:** regras analíticas, PWA, persistência.

### F2-01C — Motor de regras analíticas

- **Regras:** `ISaftAnalyticalRule` + as regras da tabela acima; NIF mod-11
  no `Domain` (reutilizável); o endpoint passa a devolver as ocorrências das
  duas camadas.
- **Validação prevista:** um teste por regra (caso passa / caso falha), com
  ficheiros mínimos construídos nos testes; um teste de ponta a ponta sobre
  um exemplo real.
- **Fora:** PWA, persistência, SVAT.

### F2-01D — Passo de validação na PWA

- **Regras:** passo «Validar ficheiro» no `SaftImportPage`; relatório
  agrupado; bloqueio da importação com `Error` e opção «Importar mesmo
  assim»; exportação CSV; rótulos pt-PT.
- **Validação prevista:** testes de interface — relatório com erros bloqueia
  os botões e a opção de override liberta-os; um ficheiro conforme deixa
  seguir direto.
- **Fora:** persistência, SVAT.

### F2-01E — Histórico de validações

- **Regras:** tabela `SaftValidationReport` (+ filha), migração EF Core,
  gravação no endpoint, endpoint de histórico por empresa, vista no ecrã da
  empresa.
- **Validação prevista:** testes de escrita na suíte de integração; teste de
  API de leitura do histórico.
- **Fora:** SVAT.

### F2-01F — Inventário SVAT

- **Regras:** documento `docs/phase-2/F2-01F-SVAT-GAP-ANALYSIS.md` que mapeia
  os critérios do SVAT para coberto / verificável não implementado /
  dependente de exportação; sem código de produção.
- **Validação prevista:** revisão do documento; nenhuma alteração de gates.

## Decisões em aberto

- **Redistribuição dos XSD.** Confirmar os termos de utilização dos esquemas
  publicados pela AT antes de os embeber no repositório; alternativa é um
  passo de obtenção-e-cache local documentado, com verificação de hash.
- **Bloqueio vs. aviso.** A opção «Importar mesmo assim» dá liberdade ao
  contabilista mas abre a porta a importar lixo; confirmar se se quer
  registar a decisão (F2-01E) como obrigatório antes de a oferecer.
- **`schemaValid` no `inspect`.** Fazer duas passagens ao ficheiro (uma no
  `inspect`, outra no `validate`) ou só expor o campo quando o `validate`
  já correu para aquele ficheiro na sessão.
- **Versões futuras do SAF-T.** Prever o ponto de extensão para uma versão
  `1.05` sem reescrever o validador.

## Referências

- Portaria n.º 302/2016 — estrutura do SAF-T (PT) de contabilidade, versão
  1.04_01.
- Portaria n.º 1192/2009 e n.º 160/2013 — versão 1.03_01.
- [Selo de Validação AT (SVAT)](https://info.portaldasfinancas.gov.pt/pt/apoio_ao_contribuinte/Negocios/Faturacao/SAF_T_PT/SVAT/Paginas/default.aspx)
- `AccountingAI.Core/Parsing/SaftParser.cs` — verificação estrutural/analítica
  do monitor, a portar e alargar.
- [`../CERTIFICACAO_AT.md`](../CERTIFICACAO_AT.md) — enquadramento de
  certificação e o que falta para produzir SAF-T.
- ADR-0004 — validação SAF-T em camadas.
