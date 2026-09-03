# F1-24 — Navegação por Enter no registo de movimentos

## Objetivo

Aproximar o registo manual de movimentos do fluxo a que os contabilistas estão
habituados: teclar e avançar. Enter passa para o campo seguinte e, no último
campo da última linha, cria uma linha nova já com o cursor na conta.

## Regras

No editor de lançamentos (`Novo lançamento` / `Corrigir`), cada campo do
formulário regista-se por uma chave estável e o formulário trata o Enter:

- em qualquer campo do cabeçalho (tipo de documento, diário, data, documento,
  descrição, conferido) e nos campos de uma linha (conta, descrição, débito,
  crédito), Enter passa o foco ao campo seguinte pela ordem natural;
- no campo **crédito da última linha**, Enter acrescenta uma linha e coloca o
  foco na **conta** dessa nova linha;
- Enter deixa de submeter o formulário a partir destes campos (o botão *Registar
  lançamento* continua a submeter); Shift+Enter e os botões mantêm o
  comportamento normal.

Alteração apenas no cliente (`MovementsPage.tsx`); sem alteração de API, domínio
nem esquema.

## Superfície de validação local

Editor de movimentos em `/movimentos`: com o cursor num campo, Enter salta para o
seguinte; no crédito da última linha, Enter cria a linha seguinte e foca a conta.

## Validação efetuada

- teste de interface: Enter na conta da linha 1 move o foco para a descrição da linha 1; Enter no crédito da linha 2 (última) cria a linha 3 e foca a respetiva conta;
- verificação no browser: Enter em «Diário» foca «Data»; Enter em «Conta da linha 1» foca «Descrição da linha 1»; Enter no «Crédito da linha 2» passa de 2 para 3 linhas e foca «Conta da linha 3»; sem erros de consola;
- lint, vinte e sete testes de interface e build de produção da PWA;
- suite .NET completa (56 testes), `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências. (Um teste de API que pressupunha não haver lançamentos depois de agosto de 2026 passou a usar uma data futura, por o exercício já poder estar apurado e aberto na base de desenvolvimento.)

## Fora deste incremento

- navegação com Tab/Shift+Tab personalizada;
- Enter para submeter quando o lançamento está equilibrado;
- setas para cima/baixo entre linhas;
- foco automático no primeiro campo ao abrir o editor.
