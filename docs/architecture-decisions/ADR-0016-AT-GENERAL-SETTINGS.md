# ADR-0016 — Configuração geral da AT na aplicação

- Estado: aceite (o utilizador pediu a configuração em 2026-10-07)
- Data: 2026-10-07

## Contexto

Até aqui o certificado de transporte da AT e a chave pública do sistema de
autenticação só se configuravam no servidor, na secção `At` do ficheiro de
configuração ou nos *user-secrets* ([ADR-0008](ADR-0008-AT-COMMUNICATION.md)).
Para pôr o certificado de testes que a AT enviou a 2026-10-07 era preciso
editar ficheiros e correr um comando para cifrar a senha. O utilizador pediu:

- as credenciais da AT **de cada empresa** (subutilizador e senha, guardada
  cifrada) nas configurações da empresa;
- uma **configuração geral** com as pastas e os nomes dos ficheiros do
  certificado e da chave pública e a senha do certificado, também cifrada.

As credenciais por empresa já existiam
([F2-02B](../phase-2/F2-02B-AT-CREDENTIALS.md)); passaram a poder editar-se
também no ecrã da empresa.

## Decisão

- **Uma linha por instalação** (`organization.AtGeneralSettings`, migração
  `AtGeneralSettings`): pasta e nome do certificado (PFX/P12), pasta e nome da
  chave pública (CER/CRT/PEM) e a senha do certificado cifrada (`enc:v1:…`,
  a mesma Data Protection do ADR-0008). As pastas não se cifram — não são
  segredo.
- **Validação no domínio** (`AtGeneralSettings.SetLocations`): pasta e nome
  vêm os dois ou nenhum; a pasta tem de ser um caminho completo; o nome não
  pode ter pastas nem carateres inválidos; só os tipos de ficheiro esperados.
- **Precedência** (`AtSettingsResolver`): o que está na configuração geral
  prevalece; a secção `At` do servidor fica como alternativa, para as
  instalações existentes continuarem a funcionar.
  - O **certificado e a senha valem em conjunto**: havendo certificado na
    configuração geral usa-se a senha dela (ou nenhuma), nunca a do ficheiro —
    misturar abria o certificado errado com a senha errada.
  - A chave pública resolve-se à parte.
- **Cache curta** (20 s, 5 s se a base falhar) da leitura da base, com
  invalidação ao gravar: uma alteração vale logo. Se a base não responder ou
  a tabela ainda não existir, vale a secção `At`.
- **A senha nunca sai da API**: o ecrã só sabe se há uma. Em branco, mantém a
  guardada; há uma opção explícita para a retirar (certificado sem senha).
- **Quem pode alterar**: a configuração vale para **todas as empresas**, pelo
  que só quem é **dono ou administrador de alguma empresa ativa** a vê e a
  altera — a mesma regra de criar utilizadores
  ([ADR-0014](ADR-0014-ORGANIZATION-PERMISSIONS.md)). Os outros recebem
  `canEdit: false` e nenhum local; a gravação responde 403. Não existe um
  «administrador do sistema» na aplicação: se um dia houver vários gabinetes
  na mesma instalação, esta regra deixa de chegar (qualquer administrador de
  qualquer gabinete alteraria o certificado de todos) e é preciso um papel
  de instalação.
- **Credenciais por empresa**: continuam as regras do F2-02B (quem tem acesso
  à empresa e não é «Viewer»); o ecrã da empresa só as mostra a donos e
  administradores, o ecrã «Comunicação com a AT» a quem lá chegar.

## Fora desta decisão

- O ambiente do servidor (`At:Environment`) e a versão do Modelo 22
  (`At:Modelo22Version`) continuam no ficheiro de configuração.
- Os *user-secrets* e o `AT_CERTIFICATE_PASSWORD` continuam a funcionar como
  alternativa, mas deixam de ser o caminho recomendado.
- **Os ficheiros têm de existir no computador onde corre a API**: a PWA não
  os carrega, só indica onde estão.
- O serviço do Windows corre noutra conta: os valores cifrados com o chaveiro
  de uma conta não se decifram noutra (ADR-0008). A senha deve ser gravada
  pela mesma conta e máquina que a vai usar.

## Validação

- `AtGeneralSettingsTests` (domínio): locais, pares pasta + nome, caminhos
  completos, tipos de ficheiro.
- `AtGeneralSettingsServiceTests` (PostgreSQL): quem pode e quem não pode; a
  senha cifrada, mantida, substituída e retirada; uma só linha; leitura
  síncrona; os pedidos recusados não gravam nem invalidam.
- `AtCertificateProviderTests`: precedência geral / ficheiro, senha não
  misturada, chave pública.
- `FoundationApiTests`: gravação pela API com um certificado de teste a
  passar a «geral» logo a seguir (invalidação da cache), a senha nunca nas
  respostas e cifrada na base, e um contabilista recusado.
- `AtPanels.test.tsx` (interface): os dois painéis.
