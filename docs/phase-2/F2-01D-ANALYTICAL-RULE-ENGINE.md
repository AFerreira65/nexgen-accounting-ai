# F2-01D — Motor de regras analíticas do SAF-T

Quarto sub-incremento do [F2-01](F2-01-SAFT-XSD-VALIDATION.md), sobre o
modelo de [F2-01C](F2-01C-VALIDATION-MODEL-READER.md). Junta a segunda camada
de validação ao endpoint.

## Objetivo

Regras de negócio que o XSD não exprime, cada uma independente e com
severidade própria, a correr sobre o `SaftValidationModel` e a devolver
ocorrências no mesmo formato da camada de esquema.

## Regras

- **Contratos** (`Application/Saft`): `ISaftAnalyticalRule.Evaluate(model) →
  IEnumerable<SaftValidationIssue>` e `ISaftAnalyticalValidator.Validate(model)
  → lista`. `SaftAnalyticalValidator` corre todas as regras registadas.
- **`PortugueseTaxNumber`** no `Domain` — dígito de controlo de NIF/NIPC
  (nove dígitos, módulo 11, pesos 9…2).
- **Regras iniciais** (`Application/Saft/Rules`):

  | Id | Severidade | Verificação |
  |----|-----------|-------------|
  | `NIF-CHECKDIGIT` | Error / Warning | `TaxRegistrationNumber` ausente ou com dígito de controlo inválido → Error; não numérico → Warning |
  | `BASIS-KNOWN` | Error | `TaxAccountingBasis` ∈ `{C,E,F,I,P,R,S,T}` |
  | `PERIOD-RANGE` | Error / Warning | `StartDate ≤ EndDate` (Error); `FiscalYear` no ano do período (Warning) |
  | `ENTRY-COUNT` | Error | `NumberOfEntries` = transações lidas |
  | `FILE-TOTALS` | Error | `TotalDebit`/`TotalCredit` = soma das linhas e iguais entre si, **ao cêntimo** |
  | `TRANSACTION-BALANCED` | Error | cada transação com débito = crédito |
  | `ACCOUNT-REF-RESOLVES` | Error | `AccountID` das linhas existe no plano (agrupado por conta) |
  | `ENTRY-DATES-IN-PERIOD` | Warning | `TransactionDate` dentro do período do cabeçalho |
  | `ACCOUNTING-HAS-JOURNALS` | Warning | base `C`/`I` com pelo menos um diário |
  | `PRODUCT-COMPANY-TIN` | Information | `ProductID` / `ProductCompanyTaxID` / `SoftwareCertificateNumber` presentes |

  `OPENING-CLOSING-CONSISTENT` e os NIF de clientes/fornecedores ficam para
  quando o modelo os trouxer.
- **`saft/validate`** grava o corpo num ficheiro temporário (não é
  reposicionável e são duas camadas a lê-lo) e devolve as ocorrências das
  **duas** camadas na mesma lista, com as contagens recalculadas.
- Regras e validador registados em `AddSaftFileReading`.

Sem alteração de esquema de base de dados. A fase do `/foundation`
mantém-se em `F2-01`.

## Superfície de validação local

`POST .../saft/validate` num ficheiro com defeitos devolve as ocorrências
analíticas (com `ruleId`) além das do esquema.

## Validação efetuada

- `AccountingAI.Infrastructure.Files.Tests`: quarenta e quatro testes (onze
  novos) — modelo limpo não levanta nada, e cada regra assinalada num caso
  de falha (NIF, base, período/exercício, contagem, totais, transação
  desequilibrada, conta por resolver agrupada, data fora do período, base
  sem diários, identificação do produtor);
- `AccountingAI.Api.Tests`: dezanove testes (um novo — o `saft/validate`
  devolve `ruleId` de `NIF-CHECKDIGIT`, `BASIS-KNOWN` e
  `ACCOUNT-REF-RESOLVES` num ficheiro deficiente);
- `dotnet build`/`dotnet test` na solução inteira sem falhas (106 testes
  .NET); testes de arquitetura verdes;
- `AccountingAI.Web`: sessenta e três testes sem alteração;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação com a API local contra ficheiros SAF-T reais: três ficheiros
  bem formados de contabilidade dão **zero erros** analíticos (sem falsos
  positivos); o SAF-T de 62 MB da Vidreira Algarvia é validado em ~1,6 s e
  levanta apenas dezanove avisos `ENTRY-DATES-IN-PERIOD` legítimos
  (transações de 2023/2024 num ficheiro do período de 2025). Um `!=` exato
  em `FILE-TOTALS` dava dois falsos erros nesse ficheiro por diferenças
  abaixo do cêntimo — corrigido para comparar ao cêntimo.

## Fora deste incremento

- passo de validação na PWA, com bloqueio da importação e exportação CSV do
  relatório (F2-01E);
- persistência do relatório e `schemaValid` no `saft/inspect` (F2-01F);
- inventário SVAT (F2-01G).
