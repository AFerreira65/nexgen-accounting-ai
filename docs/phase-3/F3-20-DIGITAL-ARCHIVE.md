# F3-20 — Arquivo digital

Épico do Bloco D da [Fase 3](README.md). Um lançamento indicava o número do
documento de origem, mas o documento em si ficava fora da plataforma.

## Sub-incrementos

### F3-20A — Documentos, ligação ao lançamento e código QR (feito)

- **`Domain/Archive/InvoiceQrCode`**: lê a mensagem do código QR das faturas
  portuguesas.
  - **Fonte:** as *Especificações Técnicas — Código de Barras Bidimensional —
    Código QR* da AT, versão 1.0 de agosto de 2020, que regulamentam a
    Portaria n.º 195/2020. O endereço do documento no Portal das Finanças já
    não responde; foi lido de uma cópia publicada por terceiros, com o
    histórico de alterações e o índice do original.
  - **Formato:** campos `código:valor` separados por `*` e valores
    monetários com ponto decimal. Lê o NIF do emitente e o do adquirente, o
    país, o tipo, o estado, a data, o documento, o ATCUD, os espaços fiscais
    I/J/K (bases e IVA por taxa), o não sujeito, o imposto do selo, o total de
    impostos, o total, as retenções, o hash, o certificado e as outras
    informações.
  - **Validação:** recusa um texto sem A, G ou O; os outros campos
    obrigatórios em falta ficam como avisos.
  - **Testes:** os dois exemplos do documento da AT (fatura e fatura
    simplificada).
- **`Domain/Archive/DocumentAttachment`** e **`DocumentAttachmentContent`**:
  o documento e o seu conteúdo, em tabelas separadas para as listas não
  carregarem os ficheiros. Migração aditiva `20261004230200_DocumentArchive`.
  - Tipos aceites: PDF, PNG, JPEG, WebP, TIFF e XML, até 15 MB.
  - Guarda o **SHA-256** do conteúdo, único por empresa: prova a integridade
    e impede o mesmo ficheiro duas vezes.
  - Liga-se a um lançamento pela **referência** deste, como as outras
    ligações da aplicação.
  - Guarda os campos principais do código QR, quando foi lido.
- **`DocumentArchiveService`** aplica estas regras:

| Parte | Regra |
|---|---|
| Arquivar | Só o nome do ficheiro, sem a pasta; o tipo vem da extensão. Um ficheiro igual a outro já arquivado é recusado e a mensagem diz onde está. |
| Ligar | O lançamento tem de existir. |
| Conservação | Um documento ligado a um lançamento **conferido** não se apaga nem se desliga: é a prova desse lançamento. Para o mudar, reabre-se o lançamento. É uma regra da aplicação. |
| Código QR | Procura o mesmo documento no que a empresa já tem: a fatura do e-Fatura (pelo NIF e pelo número normalizado); o documento de venda, quando o emitente é a própria empresa; os lançamentos não anulados com o mesmo número de documento, a ±60 dias; e os documentos arquivados com o mesmo QR. |

- **API:**
  - `POST .../attachments?fileName=&entryReference=&qr=`, com o ficheiro no
    corpo;
  - `GET .../attachments?entryReference=&unlinked=&search=`;
  - `GET .../attachments/{id}/content`, para ver no browser, ou com
    `?download=true` para descarregar;
  - `PUT .../attachments/{id}` (`{ entryReference, notes, qrText }`);
  - `DELETE .../attachments/{id}`;
  - `POST .../invoice-qr/lookup` (`{ qr }`).
- **PWA:**
  - **Ecrã «Arquivo digital»** (`/arquivo`, grupo «Trabalho»):
    - arquivar vários ficheiros de uma vez;
    - nas imagens, o código QR é lido pelo browser com a `BarcodeDetector`
      API quando existe; noutros browsers, e nos PDF, usa-se um leitor de
      códigos ou cola-se o texto;
    - procurar um código QR, com o que já existe e a ligação direta ao
      lançamento encontrado;
    - a lista, com pesquisa, o filtro «só sem lançamento», a ligação a um
      lançamento e o botão para apagar.
  - **Detalhe do movimento:** um painel «Documentos» com os anexos e o
    botão «Anexar». O cartão «Documento de origem» leva a este painel.

**Validação efetuada**

- `NexGenAccountingAI.Domain.Tests` (`InvoiceQrCodeTests`): os exemplos 1 e
  2 do documento da AT; os campos obrigatórios em falta; uma data fora do
  formato; textos que não são QR de fatura.
- `NexGenAccountingAI.IntegrationTests` (`DocumentArchiveServiceTests`, em PostgreSQL):
  - **Arquivar:** nome sem pasta; SHA-256; dados do QR; duplicado, extensão
    e QR inválidos recusados.
  - **Procurar o QR:** encontra a fatura do e-Fatura com outra pontuação no
    número, o lançamento e o documento arquivado; reconhece uma venda da
    própria empresa.
  - **Conservação:** documento de um lançamento conferido protegido contra
    apagar e desligar; lançamento inexistente recusado.
  - **Fecho:** apagar leva o conteúdo; ler o conteúdo; sem acesso.
- `NexGenAccountingAI.Api.Tests`: lista, extensão recusada, QR inválido e
  conteúdo inexistente.
- PWA: rota e estado sem base de dados.

### Por fazer

- Ler o código QR dentro de um PDF (na página, sem imagem).
- Prazos de conservação por tipo de documento, com a fonte legal.
- Ver o documento ao lado das linhas no editor de lançamentos.
