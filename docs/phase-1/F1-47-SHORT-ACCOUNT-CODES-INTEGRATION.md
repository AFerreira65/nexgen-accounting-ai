# F1-47 — Contas de 1 e 2 dígitos são de integração

## Objetivo

Fixar a regra pedida: só as contas de 3 ou mais dígitos são de movimento; as de
1 e 2 dígitos (classe e razão) são de integração — não se lança nelas. As
subcontas (`111` Caixa A, `112` Caixa B; `121` Banco Santander, `122` BCP…) é que
recebem os lançamentos.

## Regras

- `AccountingMasterDataService.CreateAccountAsync` recusa criar uma conta de
  movimento com código de 1 ou 2 dígitos (`ArgumentException` → 400).
- `AccountingTemplateCatalog`: todas as contas de 2 dígitos do plano base passam a
  agregadoras e o plano ganha um conjunto de subcontas de movimento de 3+ dígitos
  prontas a usar (`111`, `121`, `211`, `221`, `2432`–`2436`, `621`–`626`, `711`,
  `721`, …). Exceção: `56` (Resultados transitados) e `81` (Resultado líquido do
  período), lançadas diretamente pela abertura e pelo apuramento.
- `TaxonomyCatalog.SuggestFor`: sem correspondência exata, uma subconta de
  movimento herda a taxonomia da conta-razão de 2 dígitos de que é prefixo,
  quando essa herança é inequívoca (ex. `111`/`121` herdam de `11`/`12`). As
  subcontas de clientes/fornecedores (que a taxonomia subdivide) continuam por
  classificar.

## Alteração de esquema

Migração `20260903154133_ShortAccountCodesAreIntegration` (SQL de dados, sem
alteração de estrutura): `AllowsPosting = 0` para as contas de código com 1 ou 2
dígitos, exceto `56`, `81` e as que já têm movimentos (para não deixar
lançamentos numa conta agregadora). Aplicada à base `accountingAI` — as empresas
existentes recebem também as novas subcontas do plano por reprovisionamento
idempotente no arranque.

## Superfície de validação local

No editor de movimentos, escrever `62` (ou outra conta de 2 dígitos) marca a
linha como inválida e bloqueia o registo; as sugestões passam a mostrar só
`621`–`626`, `6221`… Criar uma conta de movimento de 2 dígitos pelo ecrã de
plano de contas devolve erro.

## Validação efetuada

- build .NET Debug sem avisos nem erros;
- teste de integração SQL Server LocalDB: `CreateAccountAsync("77", allowsPosting: true)` lança `ArgumentException`; a conta `12` fica agregadora e a subconta `121` herda a taxonomia `2` (referenciais S e M); a migração consta das aplicadas; os lançamentos de teste passam a usar `111`/`121`;
- teste de API: `/foundation` indica a fase `F1-47`; a revisão de taxonomia mantém `2111001`/`2211001` por classificar;
- verificação no browser contra a base `accountingAI`: após a migração e o reprovisionamento, a empresa de demonstração tem 44 contas de movimento, todas com 3+ dígitos (mais `56` e `81`); `62` é agregadora, o editor recusa-a e a sua razão soma `6221`;
- suite .NET completa (65 testes), `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- lint, trinta e dois testes de interface e build de produção da PWA.

## Fora deste incremento

- mudar o apuramento/abertura para lançarem em subcontas de `56`/`81`;
- assistente para redistribuir por subcontas os saldos de contas de 2 dígitos com movimentos históricos;
- plano base mais completo por norma (o conjunto de subcontas atual é mínimo).
