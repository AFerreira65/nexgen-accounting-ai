# ADR-0001 — Monólito modular para a nova plataforma

- Estado: aceite para a F1-01
- Data: 2026-08-29

## Contexto

A aplicação existente é um monitor WinForms especializado em SAF-T, e-Fatura e PHC. A nova plataforma necessita de uma fundação web multiplataforma, mas ainda não existem fronteiras de negócio suficientemente estabilizadas para justificar serviços distribuídos.

## Decisão

Construir a nova plataforma como monólito modular em .NET 10:

- `Domain` contém as regras contabilísticas puras;
- `Application` contém casos de uso e contratos;
- `Infrastructure.SqlServer` implementa persistência;
- `Api` expõe contratos HTTP;
- `Worker` alojará trabalho assíncrono;
- `Web` é uma PWA React/TypeScript.

As dependências apontam sempre para o interior e são protegidas por testes de arquitetura.

## Consequências

- implantação e diagnóstico permanecem simples;
- transações contabilísticas podem manter consistência forte;
- módulos podem ser extraídos no futuro quando existirem necessidade e evidência;
- a separação lógica terá de ser mantida ativamente através de testes e revisão.

O monitor WinForms e `NexGenAccountingAI.Core` são preservados, mas não são dependências da nova fundação.
