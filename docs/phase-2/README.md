# Fase 2 — Conformidade, cruzamento de dados e integração assistida

A Fase 1 (F1-01 … F1-106) construiu a plataforma contabilística local: núcleo
`Domain`/`Application`/`Infrastructure`, PWA, multi-empresa em SQL Server,
plano de contas e taxonomias, lançamentos com conferência/anulação/correção,
balancete, razão, livro diário, demonstração de resultados, balanço, resumo de
IVA, contas correntes, apuramento e abertura de exercício, e a importação de
SAF-T (cabeçalho, plano de contas, saldos de abertura e movimentos) com
reconciliação dos totais declarados. Toda a leitura de movimentos foi movida
para endpoints paginados/agregados (F1-97 … F1-105).

A Fase 2 leva a plataforma de «lê e analisa SAF-T» para «valida, cruza e
integra com garantias». Sai do âmbito desta fase emitir documentos fiscais ou
produzir um SAF-T de faturação — isso pertence a um módulo próprio e ao
processo da Modelo 24 descrito em [`../CERTIFICACAO_AT.md`](../CERTIFICACAO_AT.md).

## Objetivo da fase

- Dar confiança formal no que entra: um SAF-T importado passa a ser validado
  contra o XSD oficial e contra um conjunto de regras analíticas, com um
  relatório rastreável até ao elemento.
- Reconciliar as três fontes que hoje vivem separadas — vendas do SAF-T,
  compras do e-Fatura e a contabilidade — e mostrar as divergências.
- Tornar a consulta direta à AT utilizável em produção, com o enquadramento
  de certificado de produtor resolvido.
- Alargar o adaptador PHC aos modos que ficaram de fora na Fase 1.
- Relatórios de gestão assistidos por IA, sempre sobre dados já reconciliados
  e com ligação ao documento/lançamento de origem.

## Âmbito (épicos)

| Épico | Título | Estado |
|-------|--------|--------|
| **F2-01** | Validação XSD e analítica do SAF-T (base para SVAT) | **Concluído** — [`F2-01`](F2-01-SAFT-XSD-VALIDATION.md); [`F2-01A`](F2-01A-XSD-STREAMING-VALIDATOR.md)–[`F2-01F`](F2-01F-VALIDATION-REPORT-PERSISTENCE.md) e o [`F2-01G`](F2-01G-SVAT-GAP-ANALYSIS.md) (inventário SVAT). Pendente só o drop-in do XSD oficial |
| F2-02 | Consulta à AT validada em testes e produção (certificado de produtor, e-Fatura) | Por especificar |
| F2-03 | Cruzamento vendas SAF-T × compras e-Fatura × contabilidade | Por especificar |
| F2-04 | Adaptador PHC: modos de agrupamento, conta corrente, stocks, contabilidade, recebimentos | Por especificar |
| F2-05 | Relatórios de gestão assistidos por IA sobre dados reconciliados | Por especificar |

A ordem reflete dependências: F2-03 e F2-05 assentam na confiança que F2-01
dá aos dados; F2-02 é independente mas partilha o cuidado com segredos e
ambientes de teste; F2-04 é isolado e pode correr em paralelo.

## Princípios herdados da Fase 1

- **Incrementos pequenos e verificáveis.** Cada épico decompõe-se em
  sub-incrementos (`F2-01A`, `F2-01B`, …) com um único objetivo, um documento
  em `docs/phase-2/`, e todos os gates verdes antes de fechar.
- **Gates.** `dotnet build`/`dotnet test` na solução, `scripts/build.ps1
  -SyntheticOnly`, `gitleaks`, e no frontend `npm run lint && npm run test &&
  npm run build`. Os testes da API são de leitura contra a base real; as
  escritas ficam na suíte de integração.
- **Ramos empilhados, nunca publicados.** Um ramo por sub-incremento, com o
  trailer `Co-Authored-By`.
- **Fronteiras de camada.** `Domain` sem dependências; `Application` só
  conhece `Domain`; a validação XSD e os leitores de ficheiro vivem em
  `Infrastructure.Files`; a API orquestra.
- **Sem regressões silenciosas de conformidade.** Nada é apresentado como
  «validado pela AT», «certificado» ou «SVAT» sem o processo formal
  respetivo. Os relatórios da plataforma dizem explicitamente que são um
  apoio e não substituem o validador oficial.
- **pt-PT em toda a interface**, com os códigos de enum da API mapeados para
  módulos de rótulos partilhados.

## Sequenciação proposta

1. **F2-01** — a fundação. Sem dados em que se confia, o resto não tem base.
2. **F2-03** — o cruzamento, logo que F2-01A–D estiverem fechados (não
   precisa da persistência do relatório nem da análise SVAT).
3. **F2-02** — em paralelo com F2-03, gerido pela disponibilidade dos
   elementos de teste da AT.
4. **F2-04** — quando houver uma instalação PHC de testes disponível.
5. **F2-05** — última, sobre o produto dos anteriores.

A fase do `/foundation` na API passou a `F2-01` em F2-01B (estava fixa em
`F1-71` desde F1-71).
