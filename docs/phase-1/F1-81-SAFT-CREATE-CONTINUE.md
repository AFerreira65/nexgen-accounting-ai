# F1-81 — Continuar a importação logo após criar a empresa do SAF-T

## Objetivo

Quando o NIF do SAF-T não correspondia a nenhuma empresa e se criava a
empresa ali mesmo, o ecrã parava com «...disponível no seletor após
recarregar. Depois disso, volte a escolher o ficheiro...». Recarregar e
voltar a escolher o mesmo ficheiro é atrito desnecessário — o ficheiro
ainda está em memória e o resto do fluxo é igual ao de uma empresa
existente.

## Regras

- Depois de criar a empresa a partir do SAF-T, o ecrã de importação:
  recarrega o contexto de trabalho (a empresa entra no seletor),
  seleciona-a e volta a ler o cabeçalho do ficheiro já em memória. Como o
  NIF passa a corresponder, o ecrã segue sozinho para os painéis de
  pré-visualização e importação do plano de contas e dos movimentos.
- `refreshWorkContext` passa a aceitar, opcionalmente, o id da empresa a
  selecionar depois de carregar — necessário porque a seleção tem de usar
  a lista de empresas acabada de trazer, não a do render anterior.
- O painel «Empresa criada» passa a ser um estado de passagem, com a nota
  a dizer que a empresa já está no seletor e selecionada e que o plano de
  contas e os movimentos estão a abrir.

Sem alteração de esquema nem de API — orquestração no cliente com o
`inspectSaft` e o `refreshWorkContext` que já existem. A fase do
`/foundation` mantém-se em F1-71.

## Superfície de validação local

Escolher um SAF-T cujo NIF não corresponde a nenhuma empresa, criar a
empresa: o ecrã segue direto para importar o plano de contas e os
movimentos, sem recarregar nem voltar a escolher o ficheiro.

## Validação efetuada

- `NexGenAccountingAI.Web`: lint, build e quarenta e cinco testes de interface
  (o teste da proposta de criação continua válido — a criação em si exige
  ligação ao SQL Server e não corre no modo de demonstração);
- `dotnet build`/`dotnet test` na solução inteira sem falhas;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`: criar uma empresa a
  partir de um SAF-T com um NIF novo passa direto para os painéis de
  contas e movimentos, com a empresa já selecionada no topo; a empresa de
  teste foi removida a seguir.

## Fora deste incremento

- guardar o progresso do ficheiro escolhido entre navegações (hoje sai-se
  do ecrã e perde-se o ficheiro).
