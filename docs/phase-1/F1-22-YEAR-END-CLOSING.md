# F1-22 — Apuramento de resultados

## Objetivo

Dar a operação de fecho que faltava: apurar o resultado de um exercício, levando
as contas das classes 6 e 7 a zero contra a conta `81 — Resultado líquido do
período`.

## Regras

`YearEndClosingService` reúne, para uma empresa e exercício:

- `PreviewAsync` — o saldo de cada conta de classe 6 e 7 do exercício (sem os
  lançamentos anulados e sem o próprio apuramento), uma linha por conta que a leva
  a zero (o oposto do saldo), a contrapartida em `81` (crédito se lucro, débito se
  prejuízo) e os totais de rendimentos, gastos e resultado líquido. Indica se o
  exercício já foi apurado e a referência do lançamento.
- `RegisterAsync` — regista o lançamento de apuramento no diário de apuramento
  (`APURAMENTO`) com o tipo de documento `AP`, datado de 31 de dezembro do
  exercício, já conferido. Recusa se o exercício já foi apurado ou se não há nada
  a apurar.

Endpoints de desenvolvimento:

```text
GET  /api/v1/development/users/{userId}/companies/{companyId}/accounting/year-end-closing?year=2026
POST /api/v1/development/users/{userId}/companies/{companyId}/accounting/year-end-closing?year=2026
```

Na PWA, o ecrã **Apuramento** (`/apuramento`) mostra os cartões de rendimentos,
gastos, resultado e estado, o lançamento de apuramento linha a linha e o botão
**Registar apuramento**. Sem ligação ao SQL Server, apresenta a pré-visualização
a partir dos movimentos em memória e o registo fica indisponível.

Sem alteração de esquema. `WorkContext` passa a expor `refreshAccounting()` para
os ecrãs recarregarem os dados contabilísticos depois de uma escrita.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- teste de domínio já existente para `DocumentNumberSeries`; teste de integração SQL Server LocalDB: a pré-visualização de 2026 dá resultado líquido 1425 € (rendimentos 2670 − gastos 1245), as linhas equilibram, `81` fica com crédito 1425 €, `6221` com crédito 1245 € e `7211` com débito 2670 €; o registo cria `AP-2026-00001` no diário `APURAMENTO`, datado de 31-12-2026; a segunda pré-visualização indica já apurado e o segundo registo é recusado;
- teste de API: `/foundation` indica a fase `F1-22`; a pré-visualização do apuramento devolve `netResult` e `lines`; um exercício inválido devolve `400`;
- teste de interface: o ecrã `/apuramento` mostra o lançamento (contas `6221`, `7211`, `81`) e mantém o botão de registo desativado em modo de demonstração;
- verificação no browser contra a API: rendimentos 2500 €, gastos 1200 €, resultado 1300 €, lançamento `6221` crédito 1200 / `7211` débito 2500 / `81` crédito 1300, totais 2500 = 2500, botão de registo ativo;
- suite .NET completa (56 testes), `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências;
- lint, vinte e cinco testes de interface e build de produção da PWA.

## Fora deste incremento

- transferência do resultado para `56 — Resultados transitados` na abertura do exercício seguinte;
- separação do resultado antes de impostos, imposto e resultado líquido;
- reabertura do exercício (anular o apuramento);
- encerramento das contas de balanço (classe 1 a 5).
