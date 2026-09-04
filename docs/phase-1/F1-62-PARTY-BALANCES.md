# F1-62 — Contas correntes de clientes e fornecedores

## Objetivo

Ver quanto cada cliente deve e quanto se deve a cada fornecedor obrigava a filtrar
o balancete por prefixo. Falta um ecrã dedicado às contas correntes.

## Regras

- Novo ecrã «Contas correntes» (`/contas-correntes`), na barra lateral entre
  «Resumo de IVA» e «Apuramento».
- Usa o balancete (`GET .../accounting/trial-balance?to=`), como os outros
  relatórios compostos no cliente; sem ligação ao SQL Server, reproduz o cálculo
  sobre os movimentos sintéticos.
- Dois painéis: **Clientes** (contas de movimento cujo código começa por `21`) e
  **Fornecedores** (começa por `22`), cada um com as contas que têm saldo
  diferente de zero, o nome e o saldo (`débito − crédito`); saldo positivo = a
  receber, negativo = a pagar.
- Um campo «Saldos até» (data de corte) passa a `to`; um botão «Limpar data»
  repõe o acumulado.
- Três cartões: total de clientes, total de fornecedores e saldo líquido. Pesquisa
  por conta/nome e exportação CSV (grupos e totais).

Sem alteração de esquema nem da API — a fase do `/foundation` mantém-se em F1-60.

## Superfície de validação local

No ecrã «Contas correntes», cada conta de movimento de clientes e fornecedores com
saldo aparece no seu painel; a data de corte limita aos movimentos até essa data;
«Exportar CSV» leva os dois grupos e os totais.

## Validação efetuada

- `AccountingAI.Web`: lint, build de produção e trinta e seis testes de interface
  (um novo: na empresa de demonstração, o painel de clientes mostra `2111001` com
  saldo 3075, o de fornecedores fica vazio porque `2211001` liquida a zero, e o
  «Exportar CSV» gera o descarregamento);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- verificação no browser contra a base `accountingAI`: na empresa do Continente,
  Clientes mostra `2111001` (Maré Clara, SA) e Fornecedores `2211001` (Norte
  Office, Lda.), com o saldo líquido a somar os dois totais; a data de corte
  `2025-12-31` esvazia os dois painéis e «Limpar data» repõe-nos; o CSV traz as
  linhas por grupo e os totais.

## Fora deste incremento

- antiguidade de saldos (30/60/90 dias);
- ligar cada conta corrente à ficha do terceiro (nome fiscal, NIF, contactos);
- conciliação de recebimentos e pagamentos.
