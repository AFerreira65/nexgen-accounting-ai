# F2-01E — Passo de validação no ecrã de importação

Quinto sub-incremento do [F2-01](F2-01-SAFT-XSD-VALIDATION.md). Traz o
relatório de validação (camadas de esquema + analítica) para o ecrã de
importação, antes de qualquer escrita.

## Regras

- `WorkContext.validateSaft(file)` chama `POST .../saft/validate` e devolve
  `SaftValidation` (`version`, `schemaAvailable`, `schemaValid`, `counts`,
  `issues`, `truncated`).
- Novo painel **«Validação do ficheiro»** no `SaftImportPage`, a seguir ao
  cabeçalho, só quando `source === 'database'`:
  - botão «Validar ficheiro»;
  - resumo (versão, estado do esquema oficial, nº de erros e avisos) e uma
    frase que diz se a importação está impedida;
  - as ocorrências agrupadas por severidade, cada uma com âmbito, regra e
    local (linha/coluna), com rótulos pt-PT
    (`src/app/saftValidation.ts`: Erro / Aviso / Informação; Cabeçalho /
    Ficheiros mestre / Movimentos / Ficheiro);
  - «Exportar relatório» — CSV do relatório (isolado em `src/app/csv.ts`).
- **Bloqueio.** Enquanto o relatório tiver `Error`, os botões que escrevem
  («Criar as N contas novas», «Importar saldos iniciais», «Importar os N
  movimentos») ficam desativados. Aparece uma opção **«Importar mesmo
  assim — assumo a responsabilidade»**; ligada, liberta os botões. Avisos
  e informações não bloqueiam.
- Validar um novo ficheiro repõe o relatório e o override.

Sem alteração de esquema nem de API. A fase do `/foundation` mantém-se em
`F2-01`. Em modo de demonstração o painel não aparece (a validação precisa
da API).

## Superfície de validação local

Abrir `/importar` numa empresa ligada à BD, escolher um SAF-T e clicar
«Validar ficheiro» mostra o relatório; um ficheiro com erros trava a
importação até se ligar «Importar mesmo assim».

## Validação efetuada

- `AccountingAI.Web`: lint, build e sessenta e três testes de interface
  (sem alteração — o painel só aparece ligado à base de dados, que os
  testes não exercitam);
- `dotnet build`/`dotnet test` na solução inteira sem falhas (106 testes
  .NET, sem alteração — incremento de frontend);
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação no browser contra a base `accountingAI`, na Almeida & Costa:
  um SAF-T com `NumberOfEntries` errado e uma conta fora do plano dá dois
  erros («ENTRY-COUNT», «ACCOUNT-REF-RESOLVES»), agrupados por severidade,
  e desativa «Importar saldos iniciais» e «Importar os N movimentos»;
  ligar «Importar mesmo assim» reativa-os. Um ficheiro limpo dá zero erros
  (só o aviso do esquema não carregado) e não mostra a opção de override.

## Fora deste incremento

- persistência do relatório e histórico por empresa, mais o `schemaValid`
  no `saft/inspect` (F2-01F);
- inventário SVAT (F2-01G).
