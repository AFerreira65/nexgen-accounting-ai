# F1-90 — Guardar a data de referência do painel entre visitas

## Objetivo

F1-72 deu ao painel de trabalho uma data de referência para os
indicadores, mas ela voltava ao presente sempre que se saía do ecrã.
Quem trabalha com um exercício fechado tinha de a repor a cada visita.

## Regras

- A data de referência dos indicadores fica guardada em `localStorage`,
  por empresa (`accounting-ai.dashboard-reference-date.{companyId}`).
- Ao abrir o painel, arranca com a data guardada dessa empresa; ao mudar
  de empresa, passa a mostrar a dela.
- «Hoje» limpa a data e apaga o valor guardado.
- Um `localStorage` bloqueado ou indisponível apenas faz a data voltar a
  arrancar vazia — nunca impede o ecrã de carregar.

Sem alteração de esquema nem de API — puramente do lado do cliente. A fase
do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Escolher uma data de referência no painel, sair e voltar: a data
mantém-se; «Hoje» limpa-a e ela não volta.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build e quarenta e oito testes de interface (um
  novo: escolher a data de referência, desmontar e voltar a montar a
  aplicação — a data mantém-se; «Hoje» limpa-a);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`: escolher uma data
  no painel e recarregar mantém-na; «Hoje» limpa-a.

## Fora deste incremento

- guardar também no servidor, como preferência do utilizador.
