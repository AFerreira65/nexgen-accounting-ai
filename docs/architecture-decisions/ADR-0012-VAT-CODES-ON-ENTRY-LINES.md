# ADR-0012 — Códigos de IVA na linha do lançamento

- Estado: aceite (decisão do utilizador em 2026-10-03)
- Data: 2026-10-03

## Contexto

A [ADR-0009](ADR-0009-ACCOUNT-VAT-CLASSIFICATION.md) classificou o IVA **por
conta**: cada conta de base (classes 6 e 7) ou de imposto (24.3) tem uma taxa,
uma natureza e uma região, e o resumo do período soma o movimento das contas
classificadas. Funciona para contabilidades importadas, mas tem dois limites:

- uma conta só pode ter **uma** taxa e **uma** natureza, o que obriga a
  desdobrar o plano de contas por taxa e por tipo de operação;
- não há detalhe **por documento** — e é isso que o mapa de conferência da
  declaração periódica, a recapitulativa e os lançamentos automáticos a partir
  do e-Fatura precisam.

Todos os programas comerciais analisados
([`ANALISE-NECESSIDADES.md`](../ANALISE-NECESSIDADES.md)) usam códigos de IVA
no lançamento.

## Decisão

- **`accounting.VatCodes`** — por empresa, um código curto (`V-NOR`,
  `C-EXI-RED`, …) que junta `Rate`, `Percentage`, `Nature` e `Region`. Reutiliza
  as enumerações da ADR-0009; não cria uma segunda taxonomia.
- **A linha do lançamento passa a poder levar um código de IVA** (F3-03B). A
  regra de leitura é a que o resumo já usa: uma linha numa conta 24.3 é
  imposto, as restantes são base.
- **O código da linha prevalece; a classificação da conta é o recurso.** Uma
  linha sem código continua a ser lida pela classificação da sua conta. Assim
  os lançamentos antigos e os importados do SAF-T não mudam de leitura, e a
  classificação por conta passa a servir também de **valor proposto** no
  editor.
- **A percentagem fica gravada no código** e é editável. Os códigos por
  omissão são criados a pedido, para a região da empresa, com as percentagens
  em vigor; a aplicação não as altera sozinha.

## Consequências

- O resumo de IVA (F2-09B) e o apuramento (F2-08) têm de passar a ler o código
  da linha antes da classificação da conta (F3-03C). Enquanto isso não estiver
  feito, o código na linha é informação guardada mas ainda não usada no resumo.
- Um código usado em lançamentos não pode mudar de taxa, natureza ou região
  sem alterar a leitura do passado: a partir do F3-03B essas alterações ficam
  bloqueadas para códigos com movimento — desativa-se o código e cria-se outro.
- As percentagens vêm de fontes secundárias (n.º 1 do artigo 18.º do CIVA,
  2026: Continente 6/13/23, Açores 4/9/16, Madeira 4/12/22). Desde o F3-04A vêm
  da tabela fiscal do ano: as do Continente confirmadas no texto do CIVA, as
  das regiões autónomas ainda de fonte secundária.
- A dedutibilidade parcial (ex.: gasóleo a 50%) e o pro rata não cabem neste
  modelo e ficam para a declaração periódica (F3-08).

## Alternativa rejeitada

Manter só a classificação por conta: não mexia no que existe, mas mantinha os
dois limites acima e afastava o produto do que o mercado faz.
