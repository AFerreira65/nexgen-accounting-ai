# ADR-0004 — Validação SAF-T em camadas

- Estado: proposto para a F2-01
- Data: 2026-09-08

## Contexto

A nova plataforma lê o SAF-T (PT) em streaming e reconcilia os totais
declarados na importação, mas não valida o ficheiro contra o esquema oficial
nem contra regras de negócio antes de escrever. Um ficheiro pode ser lido com
sucesso e mesmo assim não conformar com o XSD, ou conformar e falhar
verificações que o XSD não exprime (dígito de controlo do NIF, contagens,
totais, datas fora do período, referências a contas inexistentes).

A Fase 2 (cruzamento de fontes, relatórios com IA) precisa de confiar nos
dados importados. O Selo de Validação AT (SVAT) exige, para um programa que
produz o SAF-T de contabilidade, um comportamento de validação demonstrável.

## Decisão

- A validação de um SAF-T tem **três camadas** com resultados combinados num
  só relatório (`SaftValidationReport`):
  1. **XSD estrutural** — contra o esquema oficial da AT para a versão
     declarada (`1.04_01`, e `1.03_01` com aviso), em streaming, sem
     materializar o ficheiro, acumulando ocorrências sem abortar à primeira.
  2. **Analítica** — regras de negócio independentes, cada uma com
     identificador e severidade, que correm sempre (mesmo com o XSD a falhar)
     para o relatório ficar completo.
  3. **Orientada a SVAT** — inventário do que falta para o pedido de SVAT;
     sem código de produção enquanto a plataforma não **produzir** SAF-T.
- Os **XSD oficiais** são versionados no repositório como `EmbeddedResource`
  em `Infrastructure.Files`, sujeito a confirmação dos termos de
  redistribuição; em alternativa, obtenção-e-cache local com verificação de
  hash, documentada.
- A validação **nunca** é apresentada como validação pela AT nem como
  certificação. O relatório declara que é um apoio.
- O validador XSD vive em `Infrastructure.Files` (como os leitores); as
  regras analíticas e os contratos (`ISaftSchemaValidator`,
  `ISaftAnalyticalRule`, `SaftValidationReport`, `SaftValidationIssue`) em
  `Application`; a severidade e o dígito de controlo do NIF no `Domain`.
- Um endpoint dedicado `POST .../saft/validate` produz o relatório antes de
  qualquer escrita; a PWA usa-o como passo próprio no ecrã de importação e
  bloqueia a importação quando há ocorrências `Error`, com override
  explícito e registado.
- O relatório é persistível por ficheiro/importação (tabela versionada por
  migração EF Core, ADR-0002); as escritas são testadas na suíte de
  integração.

## Consequências

- A importação de SAF-T passa a ter um passo de validação visível e
  auditável; ficheiros não conformes deixam de entrar em silêncio.
- Há um custo de manutenção: acompanhar novas versões do esquema e dos
  critérios do SVAT. O validador prevê um ponto de extensão por versão.
- Embeber os XSD cria uma dependência de licenciamento a confirmar antes de
  fechar F2-01A; o caminho alternativo (cache local) fica documentado.
- A camada analítica sobrepõe-se em parte à reconciliação já feita na
  importação; essa lógica é consolidada nas regras para não haver duas
  implementações.
- O SVAT propriamente dito continua fora de alcance até existir um módulo que
  produza o SAF-T de contabilidade (ver `CERTIFICACAO_AT.md`).
