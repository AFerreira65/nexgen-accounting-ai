# F1-06B — Catálogo completo de taxonomias por norma

## Objetivo

Substituir as duas correspondências de taxonomia embutidas no plano base por um
catálogo completo, por referencial, transcrito da Portaria n.º 302/2016.

Ficam disponíveis:

- todas as taxonomias existentes: Taxonomia S (Anexo II) e Taxonomia M (Anexo III), com código, descrição e contas SNC associadas;
- provisionamento que classifica automaticamente as contas de movimento cuja conta SNC seja inequívoca no referencial da empresa;
- um endpoint e um catálogo na PWA para escolha manual de qualquer código.

## Origem e limites

O catálogo está em
`src/AccountingAI.Infrastructure.SqlServer/Resources/taxonomy-catalog.json`,
carregado por `TaxonomyCatalog` como recurso embutido. Foi **transcrito
automaticamente** a partir do texto oficial do Diário da República (Portaria
n.º 302/2016, de 2 de dezembro). Cada entrada tem `code`, `description` e a lista
`snc` de contas do código de contas SNC que lhe correspondem (coluna «Código SNC
base» mais os prefixos de «Incluir contas»).

O referencial `N` reutiliza os códigos da Taxonomia S; o referencial `O` usa
sempre o código 1.

A transcrição **não substitui** a leitura do diploma: alguns campos de descrição
e algumas listas secundárias de «Incluir contas» podem conter imprecisões de
extração. Antes de uso em produção, o catálogo deve ser confirmado contra o texto
oficial. Por isso a atribuição automática é conservadora.

## Regras

- `TaxonomyCatalog.SuggestFor(reference, accountCode)` só devolve um código quando **exatamente uma** entrada do referencial indica essa conta SNC; caso contrário devolve nulo e a conta fica por classificar para escolha manual.
- O `CompanyAccountingProvisioner` aplica `SuggestFor` a cada conta de movimento do plano base ao criar/provisionar a empresa.
- `AccountingMasterDataService.ApplyTemplateTaxonomySuggestionsAsync` passa a usar o catálogo completo em vez das duas correspondências antigas, mantendo a idempotência.
- `TaxonomyReviewService` calcula as sugestões pelo catálogo.

O plano base de contas não foi alterado (mantém-se em 45 contas por norma).

## Superfície de validação local

```text
GET /api/v1/development/accounting/taxonomies/{reference}   (S, M, N ou O)
```

Devolve o referencial, a origem, o aviso de verificação e a lista de códigos.

Na PWA, o ecrã **Taxonomias** indica o número de códigos do catálogo e o aviso; o
editor de contas mostra a descrição do código escrito e oferece o catálogo por
`datalist` para preenchimento assistido.

## Validação efetuada

- build .NET Release sem avisos nem erros;
- testes do `TaxonomyCatalog`: carregamento dos dois referenciais (Taxonomia S com mais de 600 códigos, Taxonomia M com mais de 300), `N` reutiliza `S`, `O` usa o código 1, e `SuggestFor` só resolve correspondências inequívocas;
- teste de API para o endpoint do catálogo (S, M, O e referencial inválido);
- teste de integração SQL Server LocalDB: o provisionamento e `ApplyTemplateTaxonomySuggestionsAsync` classificam pelo catálogo completo e a segunda aplicação não altera nada;
- lint, dez testes de interface e build de produção da PWA;
- `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- verificação linha a linha do catálogo transcrito contra o diploma;
- edição do catálogo legal pela aplicação;
- validação SVAT/XSD do SAF-T;
- ecrãs de empresas e utilizadores (incremento seguinte).
