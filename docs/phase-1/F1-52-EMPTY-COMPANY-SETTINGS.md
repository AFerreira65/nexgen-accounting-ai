# F1-52 — Empresa sem preferências não bloqueia o ecrã

## Objetivo

Corrigir: ao escolher uma empresa recém-criada (associada ao utilizador mas sem
preferências gravadas), a PWA ficava com o ecrã em branco.

## Causa

O contexto de trabalho devolvia `settings: null` para uma associação sem linha em
`identity.UserCompanySettings`. A PWA lê `activeCompany.settings.theme` /
`.compactMode` / `.textSize` num efeito ao mudar de empresa; com `settings` nulo o
efeito lançava, o React desmontava a árvore e o ecrã ficava vazio.

## Regras

- O `GET .../work-context/{userId}` passa a devolver preferências por omissão
  (`pt-PT`, `Europe/Lisbon`, exercício `null`, tema `System`, modo compacto
  desligado, 50 linhas, texto `Normal`) quando a associação não tem linha de
  preferências — em vez de `null`. O `GET .../settings` continua a devolver `null`
  (o ecrã de preferências trata a ausência).
- A PWA, ao carregar o contexto, garante na mesma o objeto `settings` para cada
  empresa como defesa adicional.

Gravar preferências numa empresa nova continua a criar a linha na primeira vez.

Sem alteração de esquema.

## Superfície de validação local

Mudar para uma empresa nova (sem preferências) no seletor mostra o painel de
trabalho normalmente, incluindo o dashboard, os movimentos e as preferências.

## Validação efetuada

- build .NET Debug sem avisos nem erros;
- teste de API: `/foundation` indica a fase `F1-52`;
- verificação no browser contra a base `accountingAI`: mudar para `TNMC Lda` (empresa sem preferências) mostra o dashboard, os movimentos, o balancete, o livro diário, o plano de contas e as preferências — sem ecrã em branco; a leitura do contexto devolve o objeto `settings` por omissão para essa empresa;
- suite .NET completa (70 testes), `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem segredos;
- lint, trinta e dois testes de interface e build de produção da PWA.

## Fora deste incremento

- criar a linha de preferências ao associar a empresa (hoje é criada na primeira gravação);
- limite de erros de render com um limite de erro (`error boundary`) global na PWA.
