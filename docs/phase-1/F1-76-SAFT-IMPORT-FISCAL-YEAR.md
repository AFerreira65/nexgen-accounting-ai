# F1-76 — Exercício definido ao importar um SAF-T

## Objetivo

Depois de importar um SAF-T de 2025, o topo da aplicação continuava a
mostrar «Exercício atual» em vez de 2025 — e, por não haver nenhum
exercício definido, os atalhos de período (Mês/Trimestre/Ano) nos
relatórios caíam no ano corrente do sistema (2026), não no ano dos dados
acabados de importar. Reportado ao testar a importação da Vidreira
Algarvia.

## Regras

- `UserCompanySettings` ganha `SetDefaultFiscalYear(fiscalYear)`: só toca
  no exercício predefinido, sem mexer nas restantes preferências.
- Os três pontos de importação que já sabem o exercício do ficheiro
  (`accounts-import`, `opening-balances-import` — já recebia o ano — e
  `entries-import`) passam a marcá-lo como o exercício do utilizador
  nesta empresa depois de importar com sucesso; a empresa nunca fica sem
  exercício depois de um SAF-T, mesmo que esse ano não tivesse sido aberto
  antes. Um pedido sem o parâmetro `fiscalYear` (`accounts-import` e
  `entries-import` aceitam-no como opcional) mantém o comportamento
  anterior — não é obrigatório para as chamadas existentes.
- Sempre que o ano muda, é o do ficheiro que fica — reimportar um ano
  diferente move o exercício para esse ano.
- A PWA volta a carregar o contexto de trabalho depois de qualquer destes
  três passos, para o «Exercício» no topo e os atalhos de período
  refletirem logo o ano importado, sem recarregar a página.

Sem alteração de esquema. A fase do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Ao importar o plano de contas ou os movimentos de um SAF-T de um exercício
que a empresa ainda não tinha, o «Exercício» no topo passa a mostrar esse
ano e os atalhos de período dos relatórios usam-no.

## Validação efetuada

- `AccountingAI.Domain.Tests`: `SetDefaultFiscalYear` muda só o exercício
  e preserva as restantes preferências; rejeita anos fora do intervalo
  2000–2200 (35 testes no total, dois novos);
- `dotnet build`/`dotnet test` na solução inteira sem avisos nem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na empresa Vidreira
  Algarvia: antes da correção, o topo mostrava «Exercício atual»
  (2026, o ano do sistema); depois de reimportar o plano de contas e os
  movimentos do SAF-T de 2025, passou a mostrar «Exercício 2025» sem
  recarregar a página, e o atalho «Ano» em Resultados passou a usar 2025.

## Fora deste incremento

- fazer o mesmo na criação da empresa a partir do SAF-T (`saft/companies`)
  — hoje só se aplica a uma empresa já existente;
- guardar todos os anos alguma vez importados (não só o mais recente), para
  um seletor de exercício com histórico em vez de um único valor.
