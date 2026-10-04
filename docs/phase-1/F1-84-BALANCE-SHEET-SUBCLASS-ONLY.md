# F1-84 — Balanço: subtotais por subclasse quando há contas a mais

## Objetivo

O balancete já se protege de uma empresa grande (F1-79), mas o Balanço
não: desenhava uma linha por conta debaixo de cada subclasse. A classe 2
de uma empresa importada de SAF-T traz milhares de contas de clientes e
fornecedores — o ecrã ficava sem resposta.

## Regras

- Acima do tamanho de página das preferências da empresa (contando as
  contas das três secções — ativo, passivo, capital próprio), o Balanço
  mostra só os cabeçalhos e subtotais por subclasse; cada cabeçalho indica
  quantas contas resume.
- Um aviso remete para o balancete (paginado e pesquisável) para ver as
  contas; a exportação CSV continua a incluí-las.
- Abaixo do limite, o comportamento é o de antes (contas visíveis por
  subclasse).
- Os subtotais e os totais são sempre do conjunto inteiro.

Sem alteração de esquema nem de API — puramente do lado do cliente. A fase
do `/foundation` mantém-se em F1-71. (A demonstração de resultados fica de
fora: as classes 6 e 7 são naturalmente poucas contas.)

## Superfície de validação local

Num balanço com mais contas do que o tamanho de página, o ecrã abre de
imediato só com os subtotais por subclasse e o aviso a apontar para o
balancete.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build e quarenta e seis testes de interface (o
  teste do balanço continua válido — a empresa de demonstração tem poucas
  contas, por isso mostra-as na mesma);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na empresa Vidreira
  Algarvia: o Balanço abre de imediato só com os subtotais por subclasse,
  com o aviso e a ligação ao balancete.

## Fora deste incremento

- expandir uma subclasse específica para ver só as contas dela;
- paginação do lado do servidor.
