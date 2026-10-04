# F2-01B — Endpoint de validação de esquema do SAF-T

Segundo sub-incremento do épico [F2-01](F2-01-SAFT-XSD-VALIDATION.md), sobre
o validador de [F2-01A](F2-01A-XSD-STREAMING-VALIDATOR.md).

## Objetivo

Expor a validação de esquema num endpoint próprio, para a interface e para
testes poderem pedir o relatório antes de qualquer escrita.

## Regras

- Novo `POST /api/v1/development/users/{userId}/saft/validate` — corpo = XML
  (`application/xml`), como os restantes endpoints SAF-T. Verifica que o
  utilizador existe (404 se não), levanta o limite de corpo do pedido e
  chama `ISaftSchemaValidator.ValidateAsync`.
- Resposta `200` com o relatório:

  ```json
  {
    "version": "1.04_01",
    "schemaAvailable": false,
    "schemaValid": false,
    "counts": { "error": 0, "warning": 1, "information": 0 },
    "issues": [
      { "ruleId": null, "severity": "Warning", "scope": "Ficheiro",
        "message": "Não há esquema oficial carregado …", "line": null, "position": null, "path": null }
    ],
    "truncated": false
  }
  ```

  `severity` sai como texto (`Information`/`Warning`/`Error`).
- XML mal formado → `400` com a mensagem em pt-PT; XML válido mas **não
  conforme** → `200` com `schemaValid: false` (não é um erro do pedido).
- A fase do `/foundation` passa de `F1-71` a **`F2-01`**.

Sem alteração de esquema de base de dados.

## Superfície de validação local

`POST .../saft/validate` com um ficheiro devolve o relatório de esquema;
`GET /api/v1/foundation` devolve `"phase":"F2-01"`.

## Validação efetuada

- `NexGenAccountingAI.Api.Tests`: dezoito testes — o teste da fase passa a exigir
  `F2-01`; novo teste do `saft/validate` (ficheiro bem formado → `200` com
  `version` 1.04_01, `schemaAvailable:false`, `schemaValid:false` e um aviso;
  XML mal formado → `400`; utilizador desconhecido → `404`);
- `dotnet build`/`dotnet test` na solução inteira sem falhas (88 testes
  .NET);
- `NexGenAccountingAI.Web`: sessenta e três testes sem alteração;
- `scripts/build.ps1 -SyntheticOnly` sem ocorrências e `gitleaks` sem
  segredos;
- verificação com a API local: `/api/v1/foundation` devolve `F2-01`;
  `saft/validate` num exemplo real de 18 KB responde em ~40 ms e no SAF-T de
  62 MB da Vidreira Algarvia (passagem de boa formação em streaming) em
  ~440 ms, sem esgotar memória; XML mal formado devolve `400` com mensagem
  legível.

## Fora deste incremento

- o campo `schemaValid` no `saft/inspect` — adiado para F2-01D, onde o
  fluxo da PWA decide quando corre a validação (evita transformar o
  `inspect`, hoje uma leitura só do cabeçalho, numa leitura do ficheiro
  inteiro; liga-se à decisão em aberto n.º 3 do F2-01);
- regras analíticas (F2-01C);
- passo de validação na PWA (F2-01D).
