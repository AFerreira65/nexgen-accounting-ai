# F1-74 — Dispensar o lembrete de IVA nas Tarefas

## Objetivo

F1-73 deixou por fazer «marcar o lembrete como dispensado até à próxima
entrega». Nem toda a posição de IVA a entregar é uma surpresa — depois de
verificada ou já entregue por fora da aplicação, o utilizador deve poder
tirá-la das Tarefas sem perder o aviso caso a posição volte a mudar.

## Regras

- Botão «Dispensar até à próxima entrega» na secção «IVA a entregar»,
  junto da ligação ao Resumo de IVA.
- A dispensa fica em `localStorage`, por empresa e pelo valor exato (em
  cêntimos) da posição no momento — não por período nem por data: assim
  que a posição de IVA mudar (novo movimento, correção ou uma entrega real
  que a reduza), mesmo que volte a ser positiva, o lembrete reaparece,
  porque já não é a mesma posição que foi dispensada.
- Falha a aceder ao `localStorage` (privado, bloqueado) só faz o lembrete
  voltar a aparecer sempre — nunca impede o ecrã de carregar.
- «Tudo em dia» conta uma posição dispensada como resolvida.

Sem alteração de esquema nem de API — a fase do `/foundation` mantém-se em
F1-71.

## Superfície de validação local

Em «Tarefas», dispensar o lembrete de IVA esconde-o mesmo depois de sair e
voltar ao ecrã; um movimento novo que mude a posição de IVA volta a mostrá-lo.

## Validação efetuada

- `AccountingAI.Web`: lint, build e trinta e oito testes de interface (o
  teste das Tarefas passa a desmontar e voltar a montar a aplicação depois
  de dispensar, confirmando que o lembrete continua escondido);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`: dispensar o
  lembrete de 598,00 € esconde a secção; recarregar a página mantém-na
  escondida.

## Fora deste incremento

- dispensar os outros lembretes (movimentos por conferir, contas por
  classificar);
- persistir a dispensa no servidor em vez de por navegador.
