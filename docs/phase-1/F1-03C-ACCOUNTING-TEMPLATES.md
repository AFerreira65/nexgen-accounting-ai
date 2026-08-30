# F1-03C — Configuração contabilística inicial por empresa

## Objetivo

Uma empresa nova deixa de nascer sem configuração contabilística. A criação recebe uma norma, copia um plano de contas base e cria diários e tipos de documento operacionais. A cópia pertence à empresa e pode evoluir sem alterar o catálogo de origem nem outras empresas.

## Normas disponíveis

O catálogo inicial distingue:

- `NCRF`: SNC — regime geral;
- `NCRF-PE`: SNC — pequenas entidades;
- `NCRF-ME`: SNC — microentidades;
- `NCRF-ESNL`: SNC — entidades do setor não lucrativo;
- `IAS-IFRS-UE`: normas internacionais adotadas pela União Europeia.

A [Portaria n.º 218/2015](https://diariodarepublica.pt/dr/detalhe/portaria/218-2015-69861874) aprova um Código de Contas comum às entidades sujeitas ao SNC, com especificidades para microentidades e entidades do setor não lucrativo, e admite a sua utilização por entidades que apliquem normas internacionais. Por isso, os cinco catálogos partilham uma estrutura base, mas têm versão própria e diferenças explícitas onde o enquadramento o exige.

Este primeiro catálogo contém 45 contas agregadoras e de movimento. É uma base operacional controlada, não a reprodução exaustiva de todas as subcontas possíveis nem substitui a parametrização validada pelo contabilista certificado.

O SNC-AP não foi incluído. A [Comissão de Normalização Contabilística](https://www.cnc.min-financas.pt/sncap2017.html) trata-o como um sistema próprio, com contabilidade orçamental e Plano de Contas Multidimensional; incluí-lo exigirá um incremento autónomo.

## Diários e documentos predefinidos

Todas as normas recebem oito diários iniciais: operações diversas, compras, vendas, caixa, bancos, abertura, apuramento e encerramento.

São também criados onze tipos de documento, já associados ao diário sugerido: abertura, fatura e nota de crédito de fornecedor, fatura e nota de crédito a cliente, recebimento, pagamento, transferência, operação diversa, apuramento e encerramento.

Estes códigos são convenções operacionais do Accounting AI, não uma lista imposta pelo SNC. Depois da cópia, cada empresa poderá desativar, renomear ou ampliar os seus registos quando a respetiva funcionalidade de manutenção for implementada.

## Regras de provisionamento

- A norma é obrigatória e fica bloqueada para alteração depois do primeiro provisionamento.
- Contas, diários e tipos de documento são copiados para tabelas próprias da empresa.
- Cada registo guarda a versão do catálogo de origem.
- O código é único dentro da empresa.
- Uma segunda execução acrescenta apenas registos em falta.
- A criação da empresa e a configuração contabilística decorrem na mesma transação.
- Os tipos de documento referenciam um diário da mesma empresa por chave estrangeira composta.

O seed de desenvolvimento aplica o mecanismo às duas empresas sintéticas: regime geral no Continente e microentidades nos Açores. Os movimentos já existentes permanecem dados de teste e não provam criação de lançamentos em produção.

## Superfície de validação local

Em `Development`, `GET /api/v1/development/users/{userId}/companies/{companyId}/accounting/setup` devolve a norma, versão, contas, diários e tipos de documento da empresa, depois de confirmar a associação ativa do utilizador.

Os testes validam migrações SQL Server LocalDB, idempotência, isolamento por empresa e diferenças entre NCRF, NCRF-ME e NCRF-ESNL. Esta evidência é local e sintética; não valida parametrizações fiscais, certificação, contas específicas de um cliente ou uma instalação de produção.
