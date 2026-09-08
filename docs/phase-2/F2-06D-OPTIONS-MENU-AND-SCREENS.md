# F2-06D — Menu «Opções», `RecordContext` e ecrãs

Quarto sub-incremento do [F2-06](F2-06-DATA-DICTIONARY-AND-RECORD-INFO.md).
Leva o dicionário de dados ([F2-06C](F2-06C-DATA-DICTIONARY-SERVICE.md)) e o
«Sobre o Registo» ([F2-06B](F2-06B-RECORD-INFO-ENDPOINT.md)) à interface.

## Regras

- **`RecordContext`** (`src/app/RecordContext.tsx`) — dois contextos (valor +
  setter estável, para quem só publica não re-executar em cadeia). Um ecrã de
  detalhe chama `usePublishRecord(table, id, label)`; publica ao entrar, limpa
  ao sair. `MovementPage` → `movimentos`, `AccountPage` → `contas`,
  `JournalPage` → `diarios`.
- **`screenTable.ts`** — `pathname → { schema, name }` da tabela dominante de
  cada ecrã (para o menu abrir o dicionário já na tabela certa). `null` nos
  ecrãs sem tabela (Visão geral, Tarefas).
- **`OptionsMenu`** (`src/app/OptionsMenu.tsx`) — na barra de topo do
  `AppLayout`, **em todos os ecrãs**. Botão «Opções» com um menu:
  - **Dicionário de dados** → navega para
    `/dicionario-de-dados?tabela=<schema>/<name>` (do `screenTable` do ecrã
    atual; sem tabela, abre a página sem selecção).
  - **Sobre o Registo** → abre um painel com quem criou / quem alterou o
    registo em foco (chama `record-info`); desativado quando não há registo em
    foco ou não há ligação ao SQL Server, com uma dica no `title`. Autor
    desconhecido aparece como «origem desconhecida»; sem alteração, «Sem
    alterações registadas desde a criação».
- **`/dicionario-de-dados`** (`DataDictionaryPage`) — selector de tabela
  agrupado por esquema (`<optgroup>`), painel de detalhe com o propósito e a
  tabela de colunas (Coluna / Tipo / Nulo / Chave / Referência / Descrição),
  «Imprimir / PDF» (`window.print()` + folha de impressão que esconde a
  navegação e a barra de topo) e «Exportar CSV». Lê `?tabela=schema/name` para
  a selecção inicial; sem parâmetro, escolhe a primeira tabela.
- `WorkContext`: `recordInfo(companyId, table, id)`, `dataDictionary()`,
  `dataDictionaryTable(schema, name)`.

Sem alteração de esquema nem de API.

## Superfície de validação local

Abrir um movimento/conta/diário, carregar «Opções → Sobre o Registo»; carregar
«Opções → Dicionário de dados» em qualquer ecrã.

## Validação efetuada

- `AccountingAI.Web`: lint, build e setenta e sete testes de interface (dois
  novos — a página `/dicionario-de-dados` mostra o cabeçalho; o menu «Opções»
  aparece em todos os ecrãs com as duas ações, e «Sobre o Registo» fica
  desativado sem ligação à base de dados);
- `dotnet build`/`dotnet test` na solução inteira (cento e trinta e cinco
  testes .NET, sem alteração — incremento de frontend);
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem
  ocorrências;
- verificação no browser contra `accountingAI` (empresa Vidreira Algarvia):
  «Opções → Sobre o Registo» num movimento importado mostra «Criado por origem
  desconhecida · 08/09/2026 16:00» e «Sem alterações registadas»; «Opções →
  Dicionário de dados» a partir de `/movimentos/…` abre já em
  `accounting.JournalEntries` (17 colunas, com `JournalId → Journals`,
  `DocumentTypeId → AccountingDocumentTypes`), e o selector muda para
  `organization.Companies` (16 colunas).

## Fora deste incremento

- afinar a folha de impressão e testar a saída em PDF — F2-06E (a folha de
  impressão básica já vai aqui; F2-06E é o polimento e a verificação).
