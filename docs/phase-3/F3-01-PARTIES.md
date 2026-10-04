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

- `NexGenAccountingAI.Domain.Tests`: `PartyTests` — normalização, país por omissão,
  identificação em falta ou malformada, edição.
- `NexGenAccountingAI.IntegrationTests`: `PartyServiceTests` — criação com subconta,
  contribuinte estrangeiro, NIF inválido, duplicado, conta desconhecida e de
  integração, lista (ordem, função, pesquisa, página), edição, inativos, sem
  acesso; `SqlServerMigrationTests` inclui a migração.
- `NexGenAccountingAI.Api.Tests`: `PartiesListIsPagedAndValidatesInput` (só leitura).
- `NexGenAccountingAI.Web`: `tsc`, `eslint`, e o teste do ecrã em `App.test.tsx`.
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

- `NexGenAccountingAI.Infrastructure.Files.Tests`: `SaftPartiesReaderTests` —
  clientes e fornecedores com conta e país, ficheiro sem terceiros, XML
  malformado, e o `Customer` dentro de `SourceDocuments` não é lido.
- `NexGenAccountingAI.IntegrationTests`: `PartyImportServiceTests` — a análise não
  escreve; a importação cria, funde e completa sem mudar o nome; reimportar
  não altera nada; e-Fatura com duas faturas do mesmo emitente; sem acesso.
- `NexGenAccountingAI.Api.Tests`: `PartiesImportPreviewsAnalyseWithoutWriting` (só
  as análises, que não escrevem).
- O leitor foi corrido contra os oito ficheiros de `SAFTs Exemplo`: as
  contagens batem com o ficheiro (ex.: 815 clientes e 271 fornecedores, todos
  com conta, no SAF-T de contabilidade de 2024).
- **Por verificar em direto:** o painel «Importar» no navegador — o ecrã exige
  a base real e uma sessão iniciada pelo utilizador.

### F3-01C — Saldos e extrato por terceiro (feito)

Com o terceiro na linha do lançamento (F3-02), o saldo de um terceiro deixa de
depender de ele ter uma subconta própria.

- **`PartyLedgerService`** — só lê, sem os lançamentos anulados; o saldo é
  débito menos crédito (positivo a receber, negativo a pagar).
  - `BalancesAsync` — por terceiro com movimento: débito, crédito e saldo até
    uma data, com pesquisa, paginação no servidor e os totais do conjunto
    filtrado inteiro.
  - `StatementAsync` — o extrato de um terceiro: saldo anterior ao período, as
    linhas (data, movimento, documento, conta, descrição) com o **saldo
    acumulado**, e os totais do período. O acumulado de uma página parte do
    saldo anterior mais o movimento das páginas que a precedem.
- **`PartyService.ListAsync`** — filtro opcional por `accountCode`: os
  terceiros ligados a uma subconta.
- **API:** `GET .../parties/balances?to=&search=&page=&pageSize=`,
  `GET .../parties/{id}/statement?from=&to=&page=&pageSize=`, e
  `accountCode` em `GET .../parties`.
- **PWA:**
  - ecrã **«Saldos de terceiros»** (`/saldos-terceiros`, grupo «Mapas») —
    pesquisa, data de corte, totais e CSV; cada terceiro abre o extrato;
  - ecrã **«Extrato de terceiro»** (`/terceiros/{id}`) — período com os
    atalhos habituais, saldo anterior, movimento e saldo final, linhas com
    ligação ao movimento e à conta, CSV; também se chega pelo nome na lista de
    «Terceiros»;
  - no **editor de lançamentos**, ao escolher uma subconta 21 ou 22, o
    terceiro ligado a essa conta é **proposto** — só quando é um único e a
    linha ainda não tem terceiro.

O ecrã «Contas correntes» (saldo por subconta, a partir do balancete) fica como
está: responde a outra pergunta, e continua a ser o único que mostra os
lançamentos sem terceiro.

**Validação efetuada**

- `NexGenAccountingAI.IntegrationTests`: `PartyLedgerServiceTests` — saldos sem o
  lançamento anulado, até uma data, com pesquisa e segunda página; extrato de
  um mês com saldo anterior e acumulado; segunda página a continuar o
  acumulado; terceiro sem movimento, desconhecido e sem acesso; terceiros por
  subconta.
- `NexGenAccountingAI.Api.Tests`: `PartyLedgerReturnsBalancesAndRejectsUnknownParties`.
- `NexGenAccountingAI.Web`: `tsc`, `eslint`, os 88 testes; os dois ecrãs foram
  abertos no navegador em modo de demonstração.
- A suite completa: 217 testes .NET.
- **Por verificar em direto:** os saldos e o extrato com dados reais, e a
  proposta do terceiro no editor.

**Limite a ter presente:** os lançamentos importados de um SAF-T **antes** de
os terceiros existirem não têm terceiro nas linhas e não aparecem aqui. Ligar o
terceiro a lançamentos já importados é o incremento seguinte (F3-01D).

### F3-01D — Ligar os terceiros a lançamentos já importados (feito)

Para as empresas cujos movimentos foram importados de um SAF-T **antes** de os
terceiros existirem: relê-se o mesmo ficheiro e preenche-se o terceiro nas
linhas que ficaram sem ele.

- **`SaftEntriesImportService.LinkPartiesAsync(…, apply)`** — para cada
  transação do ficheiro com cliente ou fornecedor:
  - procura o lançamento importado com a mesma referência (`TransactionID`),
    não anulado e com origem «importado do SAF-T»;
  - só lhe toca se as linhas **continuam iguais às do ficheiro** — a mesma
    conta e o mesmo valor, pela mesma ordem; um lançamento entretanto
    corrigido fica como está e é contado;
  - atribui o terceiro pelas mesmas regras da importação (F3-02C) e **só às
    linhas sem terceiro** — a condição repete-se na própria escrita, por isso
    nunca substitui um terceiro já indicado.
- **Não altera valores, contas nem datas.** Por isso não depende de o
  exercício estar apurado nem de o período estar bloqueado: é informação
  acrescentada, não um movimento.
- **Resultado** (`SaftPartyLinkResult`): transações com terceiro, lançamentos
  correspondentes, não encontrados e alterados, linhas ligadas, linhas que já
  tinham terceiro, e transações cujo terceiro ainda não existe.
- **API:** `POST .../saft/parties-link-preview` (só analisa) e
  `POST .../saft/parties-link`, com o ficheiro no corpo.
- **PWA:** no painel **«Importar»** do ecrã «Terceiros», depois de analisar ou
  importar os terceiros de um SAF-T, a secção «Lançamentos já importados deste
  ficheiro»: «Analisar os lançamentos» mostra as contagens e «Ligar N linhas»
  grava.

**Validação efetuada**

- `NexGenAccountingAI.IntegrationTests`: `SaftPartyLinkTests` — importa sem
  terceiros, cria o cliente, e ao reler: a análise não escreve; a ligação
  preenche só a linha da conta do cliente sem mudar o valor; o lançamento com
  linhas diferentes e a transação nunca importada são contados e não tocados;
  o fornecedor ainda inexistente é contado; repetir não liga nada de novo.
- `NexGenAccountingAI.Api.Tests`: a análise devolve `applied: false`.
- A suite completa: 218 testes .NET, 88 de interface.
- **Por verificar em direto:** a ligação numa empresa real já importada — é o
  caso de uso para que isto existe, e convém começar pela análise.
