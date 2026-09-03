# F1-23 — Abertura do exercício

## Objetivo

Fechar o ciclo iniciado no apuramento (F1-22): abrir o exercício seguinte
transportando os saldos de balanço e levando o resultado do exercício anterior a
`56 — Resultados transitados`.

## Regras

`YearOpeningService` trabalha sobre uma empresa e um exercício `year`, tomando o
exercício anterior `year − 1`:

- `PreviewAsync` — os saldos acumulados até 31 de dezembro de `year − 1` das
  contas de balanço (classes 1 a 5) e da conta `81`. Cada conta de classe 1 a 5
  com saldo dá uma linha que o reinstala (débito se devedor, crédito se credor); o
  saldo de `81` é somado a `56`. Indica o número de contas, o total, se o
  exercício anterior está apurado e se este exercício já foi aberto.
- `RegisterAsync` — regista o lançamento de abertura no diário de abertura
  (`ABERTURA`), tipo de documento `AB`, datado de 1 de janeiro de `year`, já
  conferido. Recusa se o exercício já foi aberto, se o exercício anterior ainda
  não foi apurado, ou se não há saldos para transportar.

Como o balancete equilibra e o apuramento leva as classes 6 e 7 a zero, as linhas
de abertura equilibram sempre.

Endpoints de desenvolvimento:

```text
GET  /api/v1/development/users/{userId}/companies/{companyId}/accounting/year-opening?year=2027
POST /api/v1/development/users/{userId}/companies/{companyId}/accounting/year-opening?year=2027
```

Na PWA, o ecrã **Abertura** (`/abertura`) mostra os cartões (contas a transportar,
total, estado do exercício anterior, estado da abertura), o lançamento linha a
linha e o botão **Registar abertura** — desativado enquanto o exercício anterior
não estiver apurado, em modo de demonstração, ou se já tiver sido aberto. A
pré-visualização de demonstração transporta os saldos dos movimentos em memória e
simula a transferência do resultado para `56`.

Sem alteração de esquema.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- teste de integração SQL Server LocalDB: depois de apurar 2026, a pré-visualização de 2027 indica o exercício anterior apurado, as linhas equilibram, o resultado de 2026 (1425 €) aparece em `56` a crédito e não há linhas das classes 6, 7 nem da conta `81`; o registo cria `AB-2027-00001` no diário `ABERTURA`, datado de 01-01-2027; a segunda pré-visualização indica já aberto e o segundo registo é recusado; abrir 2026 é recusado porque 2025 não está apurado;
- teste de API: `/foundation` indica a fase `F1-23`; a pré-visualização da abertura de 2027 devolve `previousYear` 2026 e `previousYearClosed`;
- teste de interface: `/abertura` transporta os saldos (contas `1211`, `56`) e mantém o botão de registo desativado em modo de demonstração;
- verificação no browser contra a API: com 2026 ainda por apurar, o ecrã transporta seis contas, assinala «Apurar antes de abrir» e mantém o botão desativado;
- suite .NET completa (56 testes), `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências;
- lint, vinte e seis testes de interface e build de produção da PWA.

## Fora deste incremento

- reabertura do exercício (anular a abertura ou o apuramento);
- separação do resultado antes de impostos, imposto e resultado líquido;
- abertura por conta agregadora em vez de conta de movimento;
- fecho e abertura encadeados numa só operação.
