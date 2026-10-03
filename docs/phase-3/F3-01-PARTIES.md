# F3-01 — Terceiros

Primeiro épico do Bloco A da [Fase 3](README.md). Até aqui os clientes e
fornecedores só existiam como subcontas 21/22; sem uma entidade com NIF e país
não há declaração recapitulativa, Modelo 30, pendentes por entidade nem
lançamentos automáticos a partir do e-Fatura.

## Desenho

- **`Domain/Accounting/Party`** — `CompanyId`, `CountryCode` (ISO alfa-2, `PT`
  por omissão), `TaxRegistrationNumber` (sem espaços, maiúsculas, até 30),
  `Name`, `IsCustomer`, `IsSupplier`, `CustomerAccountId` / `SupplierAccountId`
  (opcionais), `IsActive`. O país e o NIF definem-se na criação e não mudam.
- **`accounting.Parties`** — único por `(CompanyId, CountryCode,
  TaxRegistrationNumber)`; FK a `Companies` e a `Accounts` sem cascata; colunas
  de auditoria de F2-06A. No `record-info` (`terceiros`) e no dicionário de
  dados.
- **Um NIF português é validado pelo dígito de controlo** (`PortugueseTaxNumber`);
  um contribuinte estrangeiro só tem de estar preenchido. A validação VIES fica
  para quando houver a declaração recapitulativa.
- **As subcontas são opcionais** e têm de ser contas de movimento da empresa. O
  prefixo 21/22 é sugerido no ecrã mas não imposto — há terceiros em 27.

## Sub-incrementos

### F3-01A — Cadastro de terceiros (feito)

- **`PartyService`** — `ListAsync` (pesquisa por nome ou contribuinte, filtro
  por função, inativos a pedido, paginação no servidor até 200 por página),
  `CreateAsync`, `UpdateAsync`. `null` sem acesso ou terceiro desconhecido;
  regras falhadas saem como `ArgumentException` / `InvalidOperationException`.
- **API:** `GET .../parties?search=&role=&includeInactive=&page=&pageSize=`
  → `{ total, items }`; `POST .../parties`; `PUT .../parties/{id}`. `400` com a
  mensagem em pt-PT numa regra falhada, `404` sem acesso.
- **PWA:** ecrã **«Terceiros»** (`/terceiros`, grupo «Cadastros») — pesquisa,
  filtro Todos / Clientes / Fornecedores, «Incluir inativos», lista paginada no
  servidor e editor para criar e editar. Em modo de demonstração «Requer
  ligação ao SQL Server».
- Migração aditiva `20261003144942_Parties`.

**Validação efetuada**

- `AccountingAI.Domain.Tests`: `PartyTests` — normalização, país por omissão,
  identificação em falta ou malformada, edição.
- `AccountingAI.IntegrationTests`: `PartyServiceTests` — criação com subconta,
  contribuinte estrangeiro, NIF inválido, duplicado, conta desconhecida e de
  integração, lista (ordem, função, pesquisa, página), edição, inativos, sem
  acesso; `SqlServerMigrationTests` inclui a migração.
- `AccountingAI.Api.Tests`: `PartiesListIsPagedAndValidatesInput` (só leitura).
- `AccountingAI.Web`: `tsc`, `eslint`, e o teste do ecrã em `App.test.tsx`.
- **Por verificar em direto:** o ecrã ligado à base real — precisa de uma
  sessão iniciada pelo utilizador.

### F3-01B — Terceiros a partir do SAF-T e do e-Fatura (feito)

- **`ISaftPartiesReader` / `SaftPartiesReader`** — lê `MasterFiles/Customer` e
  `MasterFiles/Supplier` em streaming (um elemento de cada vez), com a conta
  (`AccountID`) e o país da morada de faturação; para antes de
  `GeneralLedgerEntries` e `SourceDocuments`. Serve os ficheiros de
  contabilidade e os de faturação.
- **`PartyImportService`** — `FromSaftAsync` e `FromEFaturaAsync`, ambos com
  `apply` (falso = só análise). Regras:
  - funde cliente e fornecedor com o mesmo país + contribuinte num só terceiro;
  - um terceiro que já existe **nunca muda de nome nem perde ligações** — só
    ganha a função e a subconta que lhe faltarem («A completar»);
  - a subconta só é ligada se for uma conta de movimento da empresa;
  - um país que não seja um código de duas letras (o SAF-T admite
    «Desconhecido») é tratado como Portugal;
  - ficam de fora, contados por motivo: consumidor final (`999999990`), sem
    contribuinte, sem nome, NIF português inválido, contribuinte demasiado
    longo;
  - do e-Fatura vêm os emitentes distintos das compras já importadas, como
    fornecedores portugueses.
- **API:** `POST .../saft/parties-preview` e `.../saft/parties-import` (corpo =
  o ficheiro), `POST .../efatura/parties-preview` e
  `.../efatura/parties-import`. Devolvem as contagens do conjunto inteiro, até
  200 linhas (novos e a completar primeiro) e os motivos de exclusão.
- **PWA:** botão **«Importar»** no ecrã «Terceiros» — escolhe-se um SAF-T ou as
  compras do e-Fatura, vê-se a análise e só então se confirma.

**Validação efetuada**

- `AccountingAI.Infrastructure.Files.Tests`: `SaftPartiesReaderTests` —
  clientes e fornecedores com conta e país, ficheiro sem terceiros, XML
  malformado, e o `Customer` dentro de `SourceDocuments` não é lido.
- `AccountingAI.IntegrationTests`: `PartyImportServiceTests` — a análise não
  escreve; a importação cria, funde e completa sem mudar o nome; reimportar
  não altera nada; e-Fatura com duas faturas do mesmo emitente; sem acesso.
- `AccountingAI.Api.Tests`: `PartiesImportPreviewsAnalyseWithoutWriting` (só
  as análises, que não escrevem).
- O leitor foi corrido contra os oito ficheiros de `SAFTs Exemplo`: as
  contagens batem com o ficheiro (ex.: 815 clientes e 271 fornecedores, todos
  com conta, no SAF-T de contabilidade de 2024).
- **Por verificar em direto:** o painel «Importar» no navegador — o ecrã exige
  a base real e uma sessão iniciada pelo utilizador.

### F3-01C — Contas correntes por terceiro

O ecrã «Contas correntes» passa a mostrar o terceiro ligado a cada subconta.
A ligação do terceiro à linha do lançamento é o F3-02.
