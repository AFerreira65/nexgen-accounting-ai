# F1-07 — Lançamento de movimentos

## Objetivo

Até aqui os movimentos eram só dados sintéticos de leitura. Passa a existir
criação de lançamentos contabilísticos completos pela aplicação.

Ficam disponíveis:

- registar um lançamento para a empresa ativa: diário, data, documento, descrição, estado e linhas;
- referência gerada automaticamente por diário e exercício (`{DIÁRIO}-{ANO}-{NNNNN}`);
- validação de débito igual a crédito, pelo menos duas linhas, contas de movimento existentes e ativas.

## Regras

O domínio já garantia as invariantes (`JournalEntry.AddLine` exige um único
débito ou crédito por linha; `Complete` exige pelo menos duas linhas e totais
iguais). `JournalPostingService` compõe-nas com a base:

1. valida o diário (existe e está ativo na empresa);
2. resolve as contas das linhas — têm de existir, estar ativas e ser de movimento;
3. gera uma referência sequencial única por empresa, diário e ano;
4. constrói o `JournalEntry`, adiciona as linhas, conclui-o como *por conferir* ou *conferido* e grava — tudo numa transação.

Não há alteração de esquema. O tipo de documento não é ligado ao lançamento nesta
fase (a entidade não tem essa relação); serve apenas para sugerir o diário na
interface.

## Superfície de validação local

```text
POST /api/v1/development/users/{userId}/companies/{companyId}/accounting/movements
```

Corpo: `journalCode`, `entryDate`, `documentReference`, `description`, `reviewed`
e `lines` (`accountCode`, `description`, `debit`, `credit`). A resposta tem a
mesma forma da leitura de movimentos, para a lista se atualizar. Desequilíbrio,
diário ou conta desconhecidos e ausência de associação devolvem
`ValidationProblem`/`404`.

Na PWA, o ecrã **Movimentos** ganha "Novo lançamento": um formulário com linhas
repetíveis, totais de débito e crédito e indicação de equilíbrio; o botão só fica
ativo quando a diferença é zero. Sem ligação ao SQL Server, o lançamento é
adicionado ao conjunto de demonstração.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- testes de domínio já cobrem equilíbrio e validade das linhas;
- teste de integração SQL Server LocalDB: um lançamento equilibrado grava com referência `GERAL-2026-00001` e duas linhas; desequilíbrio, conta inexistente e conta agregadora são recusados;
- teste de API para as recusas (desequilíbrio, diário e conta desconhecidos, sem associação), sem escrever na base configurada;
- lint, doze testes de interface e build de produção da PWA;
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- edição e anulação de lançamentos já registados;
- ligação do lançamento a um tipo de documento e a numeração por tipo de documento;
- anexos, conciliação e fecho de período;
- importação de movimentos a partir de SAF-T;
- autorização de produção e auditoria.
