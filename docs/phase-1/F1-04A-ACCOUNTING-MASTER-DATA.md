# F1-04A — Cadastros contabilísticos

## Objetivo

Substituir os placeholders da PWA por ecrãs ligados à configuração contabilística da empresa ativa, antes de iniciar a importação de ficheiros.

Foram disponibilizados:

- plano e abertura de contas;
- diários;
- documentos contabilísticos e respetivo diário predefinido;
- taxonomias e cobertura das contas de movimento.

Todos os dados são obtidos do endpoint de configuração contabilística e filtrados por `CompanyId`. Quando a API local não está disponível, a interface identifica explicitamente os dados de demonstração.

## Taxonomias

A [Portaria n.º 302/2016](https://files.dre.pt/gratuitos/1s/2016/12/23100.pdf) estabelece os referenciais e taxonomias usados no SAF-T contabilístico:

- `S`: Taxonomia S para SNC base;
- `N`: referencial de Normas Internacionais, usando códigos da Taxonomia S;
- `M`: Taxonomia M para microentidades;
- `O`: outros referenciais, sempre com o código 1.

Segundo as [FAQ técnicas da AT](https://info.portaldasfinancas.gov.pt/pt/apoio_contribuinte/questoes_frequentes/pages/faqs-00276.aspx), o código de taxonomia é preenchido apenas em contas cuja categoria seja de movimento (`GM`). O domínio impede a atribuição em contas agregadoras.

O provisionamento atribui automaticamente apenas as correspondências inequívocas incluídas em ambas as tabelas iniciais: código 1 para a conta 11, Caixa, e código 2 para a conta 12, Depósitos à ordem. As restantes contas permanecem visivelmente por classificar. Este incremento não inclui uma reprodução parcial apresentada como catálogo legal completo.

## Limites funcionais

Os quatro ecrãs são de consulta pesquisável nesta primeira iteração. A criação, edição, desativação e validação assistida das taxonomias serão introduzidas antes da importação contabilística.

O fluxo futuro de SAF-T real fica definido assim:

1. ler e validar o cabeçalho do ficheiro;
2. procurar uma empresa existente pelo NIF;
3. propor a criação da empresa quando não existir;
4. confirmar norma e referencial de taxonomia;
5. provisionar os cadastros;
6. pré-visualizar e só depois importar.

Um SAF-T real nunca cria silenciosamente uma empresa duplicada. O ficheiro contabilístico de 82,7 MB disponível no workspace continua apenas como evidência de leitura; não foi gravado na empresa demo nem copiado para a base.

## Fronteiras de integração

PHC não faz parte da nova plataforma. As fontes atualmente previstas são SAF-T por ficheiro, e-Fatura e extratos bancários por ficheiro com deteção extensível de formato. O certificado indicado para os testes de e-Fatura deverá ficar num secret store local; caminho, password e conteúdo nunca serão versionados ou registados em logs.

Ligações futuras a ERP deverão implementar adaptadores independentes, sem transformar nenhum ERP numa dependência do domínio contabilístico.
