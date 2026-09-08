# F2-06E — Impressão e PDF

Quinto sub-incremento do [F2-06](F2-06-DATA-DICTIONARY-AND-RECORD-INFO.md).
Fecha o épico: a saída em papel / PDF do dicionário de dados e do «Sobre o
Registo». Não há dependência nova — usa `window.print()` do navegador (que
oferece «Guardar como PDF») com folhas de estilo `@media print`.

## Regras

- **Página do dicionário** — um cabeçalho **só de impressão**
  (`.dictionary-print-head`) com «Dicionário de dados — `esquema.tabela`», o
  propósito e «N colunas · Impresso em `<data por extenso>`». Ao imprimir: a
  navegação, a barra de topo e a barra de filtros (`.no-print`) desaparecem; a
  tabela de colunas passa a ter grelha, o `<thead>` repete-se em cada página
  (`display: table-header-group`) e as linhas não se partem
  (`break-inside: avoid`).
- **«Sobre o Registo»** — um botão «Imprimir / PDF» no painel. Como o painel
  vive dentro da barra de topo (escondida na impressão), a ficha é replicada
  num bloco `.record-about-print` montado no `document.body` por portal; ao
  imprimir, `printAbout()` põe `body.printing-record`, que esconde tudo menos
  esse bloco, chama `window.print()` e limpa a classe no `afterprint` (com um
  `setTimeout` de reserva para os navegadores que não o disparam). A ficha traz
  o rótulo do registo, a tabela lógica, quem criou / alterou e quando, e a data
  de impressão.
- **Exportação CSV** do detalhe já existia desde F2-06D.

Sem alteração de esquema, de API nem de testes .NET.

## Superfície de validação local

Na página `/dicionario-de-dados`, «Imprimir / PDF» → pré-visualização de
impressão com o cabeçalho próprio e a grelha. Num movimento/conta/diário,
«Opções → Sobre o Registo → Imprimir / PDF» → uma página só com a ficha do
registo.

## Validação efetuada

- `AccountingAI.Web`: lint, build e setenta e sete testes de interface (sem
  novos — a saída de impressão depende de dados que o ambiente de testes não
  busca; verificada no browser);
- `dotnet build`/`dotnet test` na solução inteira (cento e trinta e cinco
  testes .NET, sem alteração);
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem
  ocorrências;
- verificação no browser contra `accountingAI`: o cabeçalho de impressão do
  dicionário existe no DOM, escondido no ecrã, com «Dicionário de dados —
  accounting.Accounts · … · 15 colunas · Impresso em 8 de setembro de 2026 às
  16:31»; o bloco `.record-about-print` é montado no `body`, escondido no ecrã,
  com «Criado por origem desconhecida — 08/09/2026 16:00 · Sem alterações». A
  qualidade final do PDF depende do navegador e confirma-se manualmente.

## Épico F2-06 concluído

F2-06A (colunas de auditoria) · F2-06B (`record-info`) · F2-06C (serviço e
endpoints do dicionário) · F2-06D (menu «Opções», `RecordContext`, ecrãs) ·
F2-06E (impressão / PDF). Fica em aberto, se vier a ser pedido, um editor do
texto pt-PT do dicionário na interface (hoje é mantido no código,
`DataDictionaryText`).
