# F1-80 — Seletor de exercício na barra de topo

## Objetivo

O botão «Exercício {ano}» no topo era só uma etiqueta — não fazia nada.
Com empresas a receber SAF-T de vários anos (F1-76/F1-77), é preciso poder
mudar de exercício sem ir às Definições.

## Regras

- O botão passa a abrir um menu com uma janela de oito anos à volta do
  exercício atual (do ano seguinte a seis anos atrás), com o atual
  assinalado.
- Escolher um ano grava-o como o exercício do utilizador nesta empresa
  (`saveSettings`, o mesmo que a página de Definições usa) — não há valor
  novo a inventar, é a preferência que já existia.
- Como todos os atalhos de período e valores por omissão dos ecrãs já
  leem esse exercício, mudá-lo aqui acerta logo a app inteira (o filtro de
  exercício do livro diário, os atalhos Mês/Trimestre/Ano, o ano
  pré-selecionado no apuramento e na abertura, etc.).

Sem alteração de esquema nem de API — reutiliza o endpoint de preferências.
A fase do `/foundation` mantém-se em F1-71.

## Superfície de validação local

Abrir o menu «Exercício» no topo e escolher outro ano: o rótulo muda e os
ecrãs que dependem do exercício passam a usá-lo.

## Validação efetuada

- `AccountingAI.Web`: lint, build e quarenta e cinco testes de interface
  (um novo: mudar o exercício no topo para 2024 muda o rótulo e o filtro
  de exercício do livro diário passa a 2024);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na empresa Vidreira
  Algarvia: mudar de 2025 para 2024 no menu do topo grava a preferência e
  os atalhos de período passam a apontar para 2024.

## Fora deste incremento

- guardar todos os anos alguma vez importados e listá-los aqui, em vez de
  uma janela fixa à volta do exercício atual;
- fechar o menu ao clicar fora (segue o padrão do seletor de empresa, que
  também não o faz).
