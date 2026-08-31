const SEPARATOR = ';'

type CsvCell = string | number

function formatCell(cell: CsvCell): string {
  const text = typeof cell === 'number' ? cell.toFixed(2).replace('.', ',') : cell
  return /["\r\n;]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
}

/** Serializa linhas para CSV com separador `;` e números em vírgula decimal (pt-PT). */
export function toCsv(rows: CsvCell[][]): string {
  return rows.map((row) => row.map(formatCell).join(SEPARATOR)).join('\r\n')
}

/** Nome de ficheiro estável: `{prefixo}_{empresa}_{AAAA-MM-DD}.csv`. */
export function csvFilename(prefix: string, company: string): string {
  const slug = company
    .toLowerCase()
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
  const today = new Date().toISOString().slice(0, 10)
  return `${prefix}_${slug || 'empresa'}_${today}.csv`
}

/** Descarrega o CSV com BOM UTF-8 para o Excel apresentar os acentos. */
export function downloadCsv(filename: string, csv: string): void {
  const blob = new Blob([`\ufeff${csv}`], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = filename
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  URL.revokeObjectURL(url)
}
