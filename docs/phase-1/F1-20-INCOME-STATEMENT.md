# F1-20 — Demonstração de resultados (síntese por naturezas)

## Objetivo

Dar a leitura de resultados que segue naturalmente o balancete: rendimentos
(classe 7) menos gastos (classe 6) do período, por subclasse, com o resultado
líquido.

## Regras

Novo ecrã **Demonstração de resultados** (`/resultados`). Reutiliza o endpoint do
balancete (`/accounting/trial-balance`, com o mesmo intervalo de datas opcional) e
compõe a demonstração no cliente:

- as contas de classe 7 são agrupadas pelo prefixo de dois dígitos (71 Vendas,
  72 Prestações de serviços, 75 Subsídios à exploração, 78 Outros rendimentos,
  79 Juros e rendimentos similares, …); o montante de cada conta é
  `crédito − débito` do período;
- as contas de classe 6 seguem a mesma lógica (61 CMVMC, 62 FSE, 63 Gastos com o
  pessoal, 64 Depreciações e amortizações, 68 Outros gastos, …); o montante é
  `débito − crédito`;
- cada subclasse tem um subtotal; **Resultado líquido do período** =
  total de rendimentos − total de gastos.

Exclui os lançamentos anulados. A pesquisa filtra por rubrica ou conta. A
exportação CSV reproduz as duas secções, os subtotais e o resultado.

Sem alteração de API nem de domínio.

## Superfície de validação local

Ecrã `/resultados`: cartões de rendimentos, gastos e resultado; duas tabelas
(Rendimentos / Gastos) com cabeçalho de subclasse `NN · Nome`, linhas de conta e
`Subtotal NN`; rodapé com o total e, nos gastos, o resultado líquido.

## Validação efetuada

- lint, vinte e três testes de interface (novo: `/resultados` mostra as secções Rendimentos e Gastos, a subclasse `72 · Prestações de serviços`, a conta `7211` e a linha de resultado líquido) e build de produção da PWA;
- verificação no browser contra a API: rendimentos 2500 € (subclasse 72), gastos 1200 € (subclasse 62), resultado líquido 1300 € (lucro); com data final anterior aos movimentos, ambas as secções ficam vazias;
- suite .NET completa (56 testes), `scripts/build.ps1 -SyntheticOnly` e `scripts/verify-security.ps1` sem ocorrências.

## Fora deste incremento

- demonstração de resultados por funções;
- mapeamento das rubricas da Portaria 220/2015 (linhas oficiais da DR);
- resultados financeiros e imposto separados do resultado operacional;
- balanço a partir das classes 1 a 5;
- exportação em XLSX/PDF.
