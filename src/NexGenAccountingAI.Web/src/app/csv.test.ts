import { csvFilename, toCsv } from './csv'

describe('toCsv', () => {
  it('joins fields with a semicolon and rows with CRLF', () => {
    expect(toCsv([['Conta', 'Nome'], ['11', 'Caixa']])).toBe('Conta;Nome\r\n11;Caixa')
  })

  it('writes numbers with two decimals and a comma', () => {
    expect(toCsv([[1234.5, -7, 0]])).toBe('1234,50;-7,00;0,00')
  })

  it('quotes fields with a semicolon, quote or newline and doubles inner quotes', () => {
    expect(toCsv([['a;b', 'c"d', 'e\nf', 'plain']])).toBe('"a;b";"c""d";"e\nf";plain')
  })
})

describe('csvFilename', () => {
  it('slugifies the company name and appends the ISO date', () => {
    expect(csvFilename('balancete', 'Almeida & Costa, Lda.')).toMatch(/^balancete_almeida-costa-lda_\d{4}-\d{2}-\d{2}\.csv$/)
  })

  it('strips accents and falls back when the name has no usable characters', () => {
    expect(csvFilename('razao', 'Ãção')).toMatch(/^razao_acao_\d{4}-\d{2}-\d{2}\.csv$/)
    expect(csvFilename('razao', '—')).toMatch(/^razao_empresa_\d{4}-\d{2}-\d{2}\.csv$/)
  })
})
