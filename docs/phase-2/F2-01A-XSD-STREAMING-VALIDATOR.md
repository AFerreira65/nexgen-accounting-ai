# F2-01A — Validador XSD do SAF-T em streaming

Primeiro sub-incremento do épico [F2-01](F2-01-SAFT-XSD-VALIDATION.md).

## Objetivo

A camada de validação de esquema: um SAF-T é lido em streaming e validado
contra o XSD oficial da versão declarada, acumulando as ocorrências sem
abortar à primeira e sem carregar o ficheiro inteiro. Sem endpoint nem
interface ainda — isso são F2-01B e F2-01D.

## Regras

- **Contratos** (`Application/Saft`):
  - `ISaftSchemaValidator.ValidateAsync(Stream) → SaftValidationReport`;
  - `SaftValidationReport` — `Version`, `SchemaAvailable`, `SchemaValid`,
    `Issues`, `Truncated`, com contagens calculadas por severidade;
  - `SaftValidationIssue` — `RuleId?` (nulo nas ocorrências do XSD),
    `Severity`, `Scope`, `Message`, `Line?`, `Position?`, `Path?`.
  - `SaftValidationSeverity` (`Information`/`Warning`/`Error`) no `Domain`.
- **`SaftSchemaValidator`** (`Infrastructure.Files`):
  - grava um ficheiro temporário quando o conteúdo não é reposicionável (o
    corpo de um pedido não o é) e faz **duas passagens**: uma leve para o
    namespace do elemento raiz e o `AuditFileVersion`, outra a validar
    contra o esquema;
  - `DtdProcessing.Prohibit`, `XmlResolver` nulo, `MaxCharactersFromEntities
    = 0`; o `schemaLocation` do ficheiro é ignorado — a versão decide-se
    pelo cabeçalho e, em falta, pelo namespace;
  - `ValidationEventHandler` transforma cada ocorrência do XSD numa
    `SaftValidationIssue` com linha, coluna e a secção de nível 1 corrente
    (`Header`, `MasterFiles`, `GeneralLedgerEntries`, …) como `Scope`;
  - limite de 200 ocorrências, com `Truncated` a assinalar o corte;
  - `1.03_01` é assinalado com um aviso;
  - XML mal formado lança `SaftFileFormatException`; XML válido mas não
    conforme **não** lança — vem no relatório com `SchemaValid` falso.
- **`SaftSchemaCatalog`** — indexa os esquemas pelo namespace do ficheiro.
  `SaftSchemaCatalog.Official` lê os `Schemas/SAFTPT_*.xsd` embebidos no
  assembly; os testes constroem um catálogo próprio com um esquema mínimo.
- **Esquemas oficiais como drop-in.** Os `.xsd` da AT **não** estão no
  repositório (decisão de redistribuição em aberto). A pasta
  `src/AccountingAI.Infrastructure.Files/Schemas/` tem um `README.md` a
  explicar como colocá-los; o `.csproj` embebe `Schemas/SAFTPT_*.xsd` (o
  glob não corresponde a nada até serem adicionados). Sem eles, o validador
  devolve `SchemaAvailable = false`, valida só a boa formação e avisa.
- Registado em `AddSaftFileReading` como singleton.

Sem alteração de esquema de base de dados nem de API. A fase do
`/foundation` mantém-se em F1-71 (passa a F2-01 em F2-01B).

## Superfície de validação local

Os testes de `Infrastructure.Files` cobrem o mecanismo; a validação contra
o SAF-T real fica ativa assim que os `SAFTPT_*.xsd` forem colocados.

## Validação efetuada

- `AccountingAI.Infrastructure.Files.Tests`: vinte e sete testes (nove
  novos) — ficheiro conforme (0 erros, lê a versão), violação de tipo com
  linha/coluna, elemento fora de ordem, versão sem esquema (só boa
  formação + aviso, sem exceção), versão a cair no namespace quando o
  cabeçalho não a traz, `1.03_01` assinalado, XML mal formado a lançar,
  stream não reposicionável validado via ficheiro temporário, e
  `VersionFromNamespace`;
- `dotnet build`/`dotnet test` na solução inteira sem falhas (87 testes
  .NET); testes de arquitetura verdes (camadas respeitadas);
- `AccountingAI.Web`: sessenta e três testes sem alteração (incremento de
  backend);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos.

## Fora deste incremento

- endpoint `POST .../saft/validate` e o campo `schemaValid` no
  `saft/inspect` (F2-01B);
- regras analíticas (F2-01C);
- passo de validação e bloqueio da importação na PWA (F2-01D);
- persistência do relatório (F2-01E);
- caminho do elemento (`Path`) completo nas ocorrências do XSD — hoje só a
  secção de nível 1; precisa de um seguidor da pilha de elementos.
