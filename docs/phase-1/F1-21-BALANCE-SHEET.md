# F1-21 — Balanço (síntese patrimonial)

## Objetivo

Fechar o conjunto das demonstrações primárias: a par da demonstração de
resultados (F1-20), um balanço sintético a partir do balancete.

## Regras

Novo ecrã **Balanço** (`/balanco`). Reutiliza o endpoint do balancete
(`/accounting/trial-balance`, com o mesmo intervalo de datas opcional) e classifica
cada conta de movimento pelo saldo (`balance` = abertura + período):

- **Ativo** — classes 1, 3 e 4 (montante = saldo) e as contas de classe 2 com
  saldo devedor (montante = saldo);
- **Passivo** — contas de classe 2 com saldo credor (montante = `−saldo`);
- **Capital próprio** — classe 5 (montante = `−saldo`) mais o **resultado líquido
  do período** = `−(soma dos saldos das classes 6 e 7)`.

Cada secção é agrupada por subclasse de dois dígitos com subtotais (nomes SNC
partilhados com a demonstração de resultados em `src/app/accountClasses.ts`). Como
o balancete equilibra sempre, **Ativo = Passivo + Capital próprio**; o ecrã
mostra esse controlo e o desvio. Exclui os lançamentos anulados; pesquisa por
rubrica ou conta; exporta para CSV.

Sem alteração de API nem de domínio.

## Superfície de validação local

Ecrã `/balanco`: cartões de ativo, passivo, capital próprio e equilíbrio; três
tabelas (Ativo / Passivo / Capital próprio) com cabeçalho de subclasse `NN · Nome`,
linhas de conta e `Subtotal NN`; no capital próprio, a linha de resultado líquido
e os totais «capital próprio» e «passivo e capital próprio».

## Validação efetuada

- lint, vinte e quatro testes de interface (novo: `/balanco` mostra as secções Ativo/Passivo/Capital próprio, a subclasse `21 · Clientes`, a conta `2111001`, a linha de resultado e o indicador «Ativo = Passivo + CP»);
- refatoração da demonstração de resultados (F1-20) para o mesmo módulo de nomes de classes/subclasses, sem alteração de comportamento;
- verificação no browser contra a API: ativo 2875 € (Caixa 1000, Depósitos à ordem −1476, Clientes 3075, Estado devedora 276), passivo 1575 € (Fornecedores 1000, Estado credora 575), capital próprio 1300 € (resultado do período); «passivo e capital próprio» = 2875 € = ativo, desvio 0 €;
- suite .NET completa (56 testes), `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências;
- build de produção da PWA.

## Fora deste incremento

- balanço por rubricas oficiais da Portaria 220/2015 (ativo/passivo corrente e não corrente);
- afetação do resultado a resultados transitados no fecho do exercício;
- comparativo com o período homólogo;
- exportação em XLSX/PDF.
