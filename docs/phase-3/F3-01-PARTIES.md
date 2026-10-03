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

### F3-01B — Terceiros a partir do SAF-T e do e-Fatura (a seguir)

Criar os terceiros em falta a partir de `Customers` / `Suppliers` do SAF-T e
dos emitentes das compras do e-Fatura, com pré-visualização e sem duplicar.

### F3-01C — Contas correntes por terceiro

O ecrã «Contas correntes» passa a mostrar o terceiro ligado a cada subconta.
A ligação do terceiro à linha do lançamento é o F3-02.
