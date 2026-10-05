# Esquemas oficiais da AT

Os XSD oficiais da Autoridade Tributária **não estão no repositório** (a
redistribuição está por confirmar): descarregam-se sempre da origem.

## Como os obter

```powershell
.\scripts\get-official-schemas.ps1          # descarrega os que faltam
.\scripts\get-official-schemas.ps1 -Force   # atualiza todos
```

O script deixa nesta pasta (que o git ignora):

| Ficheiro | Para quê |
|---|---|
| `SAFTPT_1.04_01.xsd`, `SAFTPT_1.03_01.xsd` | Validação dos SAF-T importados e exportados |
| `DossierFiscal.xsd` | Ficheiro dos mapas 30, 31 e 32 do dossier fiscal |
| `dpiva-06.xsd` | Declaração periódica de IVA |
| `Modelo22IRCv2026.xsd`, `types.xsd` | Modelo 22 |
| `dr-1.0.xsd` | Declaração recapitulativa |

Depois de os descarregar, **reconstruir a solução**: os `SAFTPT_*.xsd` são
embebidos no assembly e a aplicação passa a validar contra o esquema oficial.
Os testes que geram ficheiros oficiais validam-nos contra o que estiver nesta
pasta.

## O que a validação cobre

O SAF-T 1.04_01 é um esquema **XSD 1.1** e o validador do .NET só suporta
XSD 1.0. Ao carregar (`SaftSchemaCatalog.Load`):

- tiram-se as 20 regras `xs:assert` (condições entre campos, por exemplo
  «conta de movimento tem de ter taxonomia»);
- o grupo `xs:all` das linhas de um movimento passa a «um ou mais débitos ou
  créditos, por qualquer ordem» (deixa de exigir pelo menos um de cada).

A estrutura, os tipos, as obrigatoriedades e as listas de valores são
validados. O relatório diz quantas regras ficaram por verificar. Antes desta
adaptação, o esquema oficial não chegava a carregar e a validação dava erro.

## Sem os ficheiros

Sem nenhum `SAFTPT_*.xsd` na pasta, o validador **não falha**: devolve
`schemaAvailable = false`, valida só a boa formação do XML e avisa que o
esquema oficial não está carregado.

## Notas

- O catálogo indexa cada esquema pelo `targetNamespace` que ele declara.
- Não colocar aqui esquemas de terceiros nem versões modificadas.
