# Certificação e acesso aos serviços da AT

## Três processos diferentes

1. **Certificado digital para webservices (CSR/PFX)** — identifica o produtor/integrador na ligação técnica à AT. É o processo necessário para validar a consulta SOAP já existente no NexGen Accounting AI.
2. **Certificação de programa de faturação (Modelo 24)** — atribui um número de programa certificado a software que emite faturas ou outros documentos fiscalmente relevantes.
3. **Selo de Validação AT (SVAT)** — processo facultativo para programas de contabilidade que produzem o SAF-T (PT) de contabilidade.

No estado atual, o NexGen Accounting AI lê e analisa SAF-T, importa CSV e consulta compras. Não escreve na AT, não emite documentos fiscais nem produz um SAF-T de faturação ou de contabilidade. Assim, não deve ser apresentado neste estado como programa de faturação para efeitos da Modelo 24.

O manual do webservice enquadra a adesão nos produtores de programas de faturação e, no pedido dos elementos de teste, solicita a designação do software e o respetivo número de certificado AT. Por isso, não se deve assumir que a AT aceitará a adesão do NexGen Accounting AI enquanto analisador autónomo. É necessário obter confirmação escrita da AT ou associar a integração a um programa de faturação certificado. A consulta pelo Portal e a importação CSV não dependem deste certificado de produtor.

## Acesso ao webservice e-Fatura

1. Confirmar com a AT se o produto e o produtor são elegíveis para o serviço. O pedido de testes publicado pela AT identifica o software e o número de certificado de programa de faturação.
2. Registar-se no Portal das Finanças como produtor de software/integrador e aceder a **e-Fatura > Produtores de Software**.
3. Solicitar os elementos de desenvolvimento e testes indicados no manual: chave pública do sistema de autenticação e certificado SSL de testes.
4. Depois dos testes, efetuar o pedido de adesão ao serviço e gerar um CSR com RSA de 4096 bits, conforme a política atual da entidade certificadora da AT.
5. Receber da AT o certificado assinado e preparar o PFX com a respetiva chave privada, protegido por senha.
6. Criar, para cada sujeito passivo, o subutilizador e perfil WFA exigidos pela operação e-Fatura, sem usar a senha principal na aplicação.
7. Validar primeiro no ambiente de testes e só depois usar o acesso de produção.

O certificado de transporte e a sua chave privada pertencem ao produtor. Não devem ser entregues em texto, guardados no repositório ou confundidos com a chave RSA que assina documentos num programa de faturação certificado.

## Caminho futuro para certificar o NexGen Accounting AI como programa de faturação

Só se justifica iniciar este processo quando existir um módulo próprio que finalize e emita documentos com eficácia externa. Antes do pedido, a aplicação terá de incluir, entre outros pontos:

- emissão cronológica e numeração progressiva e contínua por tipo e série;
- assinatura RSA encadeada dos documentos e conservação da assinatura e da versão da chave;
- proibição de alterar informação fiscal de documentos finalizados sem evidência e procedimentos de regularização;
- autenticação individual de utilizadores e trilho de auditoria;
- impressão das menções obrigatórias do programa certificado e dos quatro carateres da assinatura;
- exportação integral de SAF-T (PT) conforme o XSD e regras técnicas aplicáveis;
- ATCUD, código QR, comunicação de séries e comunicação dos documentos à AT;
- estados, anulações, recuperação de cópias, formação, cópias de segurança e restantes regras do Despacho n.º 8632/2014.

### Dossier de conformidade

Preparar um exemplar instalável, base de dados de teste, manual, dicionário de dados, cenários de emissão/anulação/regularização, SAF-T produzido nos testes e o ficheiro PEM/TXT da chave pública correspondente à chave privada de assinatura. O SAF-T e as assinaturas devem ser verificados com a aplicação oficial de validação disponibilizada pela AT.

### Submissão

Antes da comercialização, a entidade detentora dos direitos do programa submete eletronicamente a **Declaração Modelo 24** no Portal das Finanças, indicando o NIF do produtor, nome comercial/ProductID, versão inicial, eventual depósito ASSOFT, chave pública e respetiva versão e o NIF do representante legal. A AT pode realizar testes de conformidade e solicitar o programa, documentação e dicionário de dados. Depois da aprovação, o programa passa a constar da lista oficial e o número atribuído é usado nos documentos e no SAF-T.

As versões seguintes mantêm o mesmo certificado se continuarem conformes. A alteração do par de chaves RSA ou dos restantes elementos indicados nas instruções da Modelo 24 exige declaração de substituição.

## Integração com PHC

Se o NexGen Accounting AI apenas preparar dados e os entregar ao PHC, ficando a finalização, numeração, assinatura, ATCUD, QR, comunicação e SAF-T sob o controlo da versão certificada do PHC, a fronteira de emissão continua, em princípio, no PHC. A integração não deve escrever diretamente nas tabelas fiscais nem conseguir contornar as regras do PHC. Antes de comercializar esse módulo, deve ser pedido enquadramento escrito à AT sobre a arquitetura concreta.

## Referências oficiais

- [Certificação de Software de Faturação](https://info.portaldasfinancas.gov.pt/pt/apoio_ao_contribuinte/Negocios/Faturacao/Regras_mecanismos_comunicacao/Certificacao_programas/Certificacao_software_faturacao/Paginas/default.aspx)
- [Portaria n.º 363/2010](https://info.portaldasfinancas.gov.pt/pt/informacao_fiscal/legislacao/diplomas_legislativos/Documents/Portaria_363_2010.pdf)
- [Modelo 24 e instruções](https://info.portaldasfinancas.gov.pt/pt/apoio_contribuinte/modelos_formularios/irs_irc/Documents/M24_IRC_2010.pdf)
- [Despacho n.º 8632/2014](https://info.portaldasfinancas.gov.pt/pt/informacao_fiscal/legislacao/diplomas_legislativos/Documents/Despacho_n%C2%BA_8632_2014_03_07.pdf)
- [Manual do webservice e-Fatura — aspetos gerais](https://info.portaldasfinancas.gov.pt/pt/apoio_ao_contribuinte/Outras_entidades/Suporte_tecnologico/Webservice/e_Fatura/Documents/Comunicacao_dos_elementos_dos_documentos_de_faturacao_aspetos_gerais.pdf)
- [Selo de Validação AT (SVAT)](https://info.portaldasfinancas.gov.pt/pt/apoio_ao_contribuinte/Negocios/Faturacao/SAF_T_PT/SVAT/Paginas/default.aspx)
