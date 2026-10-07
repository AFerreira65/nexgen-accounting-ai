# ADR-0017 — Utilizador produtor e configuração geral portátil da AT

- Estado: aceite (o utilizador pediu-o em 2026-10-07); substitui o modelo do
  [ADR-0016](ADR-0016-AT-GENERAL-SETTINGS.md) na parte do acesso, do local dos
  ficheiros e da cifra
- Data: 2026-10-07

## Contexto

O [ADR-0016](ADR-0016-AT-GENERAL-SETTINGS.md) pôs a configuração geral da AT
na aplicação, mas com três limites que o utilizador apontou:

1. **Quem acede.** Qualquer dono ou administrador de uma empresa a via. Para
   o utilizador, a configuração do certificado é do **produtor do software** e
   o menu de configuração só deve existir para um **utilizador especial**.
2. **Onde estão os ficheiros.** Eram caminhos no computador da API. O
   certificado é **um só para todas as empresas** e pode ser preciso em
   **qualquer posto**; por isso deve ficar **na base de dados**, para qualquer
   posto o usar sem o instalar.
3. **A cifra.** O `enc:v1:` (Data Protection com chaveiro protegido pelo DPAPI)
   só se decifra na máquina e conta que cifrou. Quando um posto precisa da
   senha do certificado, tem de a conseguir decifrar.

## Decisão

### O utilizador produtor

- `identity.UserAccounts.IsProducer` (migração `AtGeneralSettingsPortable`).
  É uma conta **à parte, sem empresas**, criada **só por linha de comandos** no
  servidor — nunca por um ecrã nem por um endpoint:

  ```
  dotnet run --project src/NexGenAccountingAI.Api -- create-producer-user "<email>" "<nome>"   (senha pelo stdin, mínimo 12 caracteres)
  dotnet run --project src/NexGenAccountingAI.Api -- remove-producer-user "<email>"
  ```

  Retirar o produtor desativa a conta. Um utilizador normal não passa a
  produtor por este caminho (recusa-se se o email já existir como normal).
- **O servidor verifica a marca na base de dados a cada pedido**
  (`AuthService.IsProducerAsync`), não no cookie: retirar o produtor vale logo.
- `/auth/login` e `/auth/me` devolvem `isProducer`. Na PWA, `AuthGate` mostra a
  quem é produtor **só** o `ProducerShell` (cabeçalho, «Sair» e a configuração
  geral): não carrega o contexto de trabalho nem o menu. Para os restantes
  utilizadores o menu e o ecrã não existem; «Comunicação com a AT» só mostra o
  estado do certificado (sem o alterar) e as credenciais da empresa.
- A conta do produtor é **invisível** à gestão de utilizadores das empresas:
  `UpdateUserAsync` responde «não existe» e `SetMembershipAsync` recusa-a, para
  um administrador de empresa não a alterar nem lhe dar acesso a dados.

### A configuração geral

- `organization.AtGeneralSettings` (uma linha) guarda agora os **bytes** do
  certificado (`bytea`, PFX/P12) e da chave pública (CER/CRT/PEM), os nomes dos
  ficheiros (só para mostrar) e a senha do certificado cifrada. Máximo 256 KB
  por ficheiro. Desapareceram as pastas.
- **Validação antes de gravar** (`IAtFileValidator`): o PFX tem de abrir com a
  senha e trazer a chave privada e não estar expirado; a chave pública tem de
  ser um certificado com chave RSA. A troca de senha só vale se abrir o
  certificado guardado. Retirar o certificado leva a senha com ele.
- **Nada disto sai da API**: as respostas trazem os nomes, se há senha e o
  estado (assunto, validade, origem). Nunca a senha, o valor cifrado nem os
  ficheiros.
- **O certificado não se instala**: cada posto carrega os bytes da base de
  dados para a memória (`EphemeralKeySet`) quando precisa, sem tocar no
  repositório de certificados do Windows. A leitura da base fica em memória 20 s
  (5 s se falhar) e é invalidada ao gravar: a alteração vale logo no posto que
  a fez e nos outros em segundos.
- **Precedência** mantém-se: o que está na base prevalece, o certificado e a
  senha em conjunto; a secção `At` do ficheiro de configuração fica como
  alternativa (`origem: ficheiro`) para instalações existentes.

### A cifra portátil (`enc:v2:`)

- AES-256-GCM, chave derivada por HKDF-SHA256 de um segredo da aplicação e de
  um **sal aleatório por valor**, nonce aleatório de 12 bytes e etiqueta de
  autenticação. O resultado é `enc:v2:` + base64(versão, sal, nonce, etiqueta,
  texto cifrado). É o que passa a escrever-se.
- **Qualquer posto** com o programa e a mesma base de dados decifra, sem
  chaveiro e sem instalar nada. Um valor alterado falha a autenticação.
- O formato anterior `enc:v1:` (Data Protection) continua a **ler-se** onde foi
  criado, para não perder as senhas das credenciais das empresas já guardadas;
  deixa de se escrever.

### O que a cifra protege e o que não protege

O segredo da aplicação está no programa, igual em todos os postos — é isso que
torna a cifra portátil. Logo:

- **protege** a base de dados e as cópias de segurança contra leitura casual
  (a senha não aparece em texto, nem num *dump*, nem num ecrã);
- **não protege** de quem tenha a base de dados **e** o programa: essa pessoa
  consegue decifrar. É o preço de qualquer posto poder decifrar sem
  configuração.

O controlo real é, por isso, de **acesso**: só a conta do produtor vê ou altera
a configuração, a API nunca devolve estes valores, e quem tem acesso direto à
base de dados já é, para este efeito, confiável. Se um dia for preciso proteger
também contra quem tem a base de dados, é preciso uma chave fora da base (por
exemplo, um cofre de segredos), com o custo de a configurar em cada posto.

## Fora desta decisão

- O ambiente do servidor (`At:Environment`) e a versão do Modelo 22 continuam
  no ficheiro de configuração.
- As credenciais por empresa (subutilizador e senha) continuam por empresa, com
  as regras do F2-02B; passam a ficar cifradas com `enc:v2:` ao gravar.
- Um papel de «administrador de instalação» mais geral não existe; o produtor
  é só para esta configuração.

## Validação

- `AtGeneralSettingsTests` (domínio): ficheiros guardados só com o nome, tipos,
  tamanho, senha ligada ao certificado, marca de produtor.
- `AtSecretProtectorTests`: ida e volta com `enc:v2:`, valores diferentes de
  cada vez, **decifra noutro posto sem o chaveiro**, `enc:v1:` só onde foi
  criado, valores adulterados ou lixo ficam como estão.
- `AtCertificateProviderTests`: certificado da base de dados sem ficheiro no
  posto, senha cifrada noutro posto, precedência, validação dos ficheiros.
- `AtGeneralSettingsServiceTests` (PostgreSQL): só o produtor ativo; ficheiros
  e senha na base, senha cifrada; troca de senha validada; retirar; pedidos
  recusados sem efeito; a conta do produtor invisível à gestão de utilizadores.
- `FoundationApiTests`: 403 a quem não é produtor, gravação pela API a passar a
  «geral» logo (invalidação), nada de segredos nas respostas, `enc:v2:` na
  base, e o acesso a acabar logo ao retirar a marca.
- `ProducerSettingsPage.test.tsx`: o ecrã do produtor, o envio em base64 e o
  ecrã sem menu da aplicação.
