# F1-59 — Balancete agrupado por taxonomia

## Objetivo

O balancete agrupa-se por classe SNC (F1-19). Falta agrupá-lo por código de
taxonomia — a vista que interessa para a IES e para conferir a cobertura das
classificações depois de importar um SAF-T.

## Regras

- A barra de filtros do balancete ganha o interruptor «Agrupar por taxonomia», a
  par de «Agrupar por classe»; ligar um desliga o outro.
- O agrupamento é do lado do cliente, como o «por classe»: cada linha do balancete
  (conta de movimento) é associada ao `taxonomyCode` da conta (do cadastro da
  empresa) e as linhas são somadas por código. O nome de cada código vem do
  catálogo de taxonomias já carregado (`taxonomyCatalog`); um código sem nome no
  catálogo aparece como «Código N».
- As contas de movimento sem código de taxonomia caem num grupo «Sem taxonomia»,
  sempre no fim, para sinalizar o que falta classificar.
- Cada grupo mostra as suas contas e uma linha de subtotal (abertura, débito,
  crédito, saldo); os totais do balancete e a comparação com o período homólogo
  continuam a funcionar.
- A exportação CSV respeita o agrupamento activo: com «por taxonomia», o ficheiro
  traz as contas por grupo e uma linha de subtotal por código.

Sem alteração de esquema nem da API — a fase do `/foundation` mantém-se em F1-56.

## Superfície de validação local

No balancete, ligar «Agrupar por taxonomia» reorganiza as contas por código de
taxonomia, com «Sem taxonomia» no fim; a exportação CSV segue o mesmo agrupamento.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build de produção e trinta e três testes de interface
  (o teste do balancete passa também a ligar «Agrupar por taxonomia», confirmar o
  subtotal «Sem taxonomia» e a exclusão mútua com «Agrupar por classe»);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- verificação no browser contra a base `accountingAI`: na empresa de
  demonstração, «Agrupar por taxonomia» mostra os grupos `1 · Caixa`,
  `2 · Depósitos à ordem`, `338 · Resultados transitados`,
  `357 · Fornecimentos e serviços externos…` e `Sem taxonomia` (por esta ordem,
  com «Sem taxonomia» no fim); a exportação CSV traz o cabeçalho «Taxonomia», uma
  linha de subtotal por grupo e os totais equilibrados (`13878,00 / 13878,00`).

## Fora deste incremento

- calcular o agrupamento por taxonomia no servidor (continua no cliente, como o
  «por classe»);
- mapear os saldos para o modelo da IES/anexos.
