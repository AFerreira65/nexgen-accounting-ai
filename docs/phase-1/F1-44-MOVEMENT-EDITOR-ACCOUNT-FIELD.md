# F1-44 — Editor de movimentos: linhas, sugestão de contas e recusa de agregadoras

## Objetivo

Três melhorias ao editor de lançamentos pedidas pela utilização:

1. reduzir ligeiramente o espaçamento entre linhas;
2. voltar a sugerir contas enquanto se escreve, mas só contas de movimento;
3. não avançar o cursor (nem deixar registar) quando a linha tem uma conta de
   integração — só contas de movimento podem ser lançadas.

## Regras

O campo de conta passa a ser um `AccountField` com sugestões próprias (deixou de
usar `<datalist>`): mostra até 8 contas de **movimento** filtradas pelo início do
código ou pelo nome, escolhe-se com o rato ou com as setas + Enter (Enter numa
sugestão realçada preenche-a e avança). As contas de integração
(agregadoras/razão) nunca aparecem na lista.

Se uma linha tiver um código que corresponde a uma conta de integração conhecida
do plano da empresa, o campo fica marcado a vermelho, o Enter não avança para o
campo seguinte e o botão de registo fica desativado com o aviso «Há linhas com
uma conta de integração; só contas de movimento podem ser lançadas.». Códigos
inexistentes continuam a ser recusados pela API no registo.

As linhas do editor ficam com menos `padding` vertical e os campos das células
ganham um estilo consistente.

Sem alteração de esquema nem de backend.

## Superfície de validação local

No **Novo lançamento**, ao escrever no campo de conta aparece a lista de contas
de movimento; escrever o código de uma conta agregadora marca a linha como
inválida e bloqueia o registo.

## Validação efetuada

- verificação no browser contra a base `accountingAI`: escrever `62` sugere as contas de movimento correspondentes; escolher uma sugestão preenche o código; escrever `1` (agregadora) marca a linha a vermelho, mostra o aviso e desativa o registo; o `padding` das linhas passou de `13px` para `6px`;
- testes de interface: a lista sugere `2211001` ao escrever `22` e a escolha preenche o campo; uma conta de integração numa linha marca-a como inválida e desativa o registo, e volta ao normal ao corrigir;
- `scripts/build.ps1 -SyntheticOnly` (suite .NET de 64 testes e smoke tests) sem ocorrências e `gitleaks` sem segredos;
- lint, trinta e um testes de interface e build de produção da PWA.

## Fora deste incremento

- marcar como agregadoras, no plano base, todas as contas de 1 e 2 dígitos (hoje o plano SNC provisionado deixa lançar em contas de 2 dígitos);
- tamanho de letra ajustável nas configurações;
- razão das contas agregadoras a somar as subcontas de movimento.
