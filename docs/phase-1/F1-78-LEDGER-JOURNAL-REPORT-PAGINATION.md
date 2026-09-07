# F1-78 — Paginação da razão de conta e do livro diário

## Objetivo

F1-75 paginou a lista de Movimentos e deixou as outras listas longas para
depois. Numa empresa grande (SAF-T com anos de histórico), o **livro
diário** desenha dezenas de milhares de linhas de uma vez e a **razão** de
uma conta de integração soma milhares de linhas das subcontas — os dois
ecrãs mais prováveis de abrir logo a seguir a uma importação.

## Regras

- Ambos usam a função partilhada `paginate` (`src/app/pagination.ts`),
  pelo tamanho de página das preferências da empresa, e voltam à primeira
  página quando um filtro (ou a conta, na razão) muda.
- **Livro diário**: pagina por lançamento — as linhas de um lançamento
  nunca se partem entre páginas. Os totais no rodapé e a contagem de
  lançamentos continuam a ser do conjunto filtrado inteiro; a exportação
  CSV também. O filtro «Exercício» deixa de ficar preso ao valor do
  primeiro desenho (muitas vezes o ano do sistema): volta a acertar ao
  trocar de empresa ou quando o exercício predefinido muda — importante
  logo a seguir a importar um SAF-T de um ano diferente do corrente.
- **Razão de conta**: pagina as linhas. O saldo acumulado de cada linha já
  vem calculado, por isso a página não o afeta; a linha «Saldo de
  abertura» só aparece na primeira página. Os cartões de saldo e a
  contagem de movimentos continuam a ser do conjunto inteiro.

Sem alteração de esquema nem de API — puramente do lado do cliente, sobre
dados já carregados. A fase do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Numa conta ou num livro diário com mais registos do que o tamanho de
página, o ecrã mostra só uma página de cada vez, com navegação Anterior/
Seguinte, sem alterar os totais nem os saldos apresentados.

## Validação efetuada

- `AccountingAI.Web`: lint, build e quarenta e quatro testes de interface
  (a paginação em si já estava coberta pelos testes unitários de
  `paginate` de F1-75);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na empresa Vidreira
  Algarvia: o livro diário passa a mostrar «Página 1 de N» e desenha de
  imediato; a razão de uma conta de integração também.

## Fora deste incremento

- paginar o balancete (tem agrupamento por classe/taxonomia e subtotais,
  que pedem um tratamento próprio);
- paginação do lado do servidor (a lista continua a carregar tudo de uma
  vez; só o desenho é que é por página).
