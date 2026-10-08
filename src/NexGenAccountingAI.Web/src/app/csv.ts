const SEPARATOR = ';'

type CsvCell = string | number

function formatCell(cell: CsvCell): string {
  const text = typeof cell === 'number' ? cell.toFixed(2).replace('.', ',') : cell
  return /["\r\n;]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
}

/** Serializa linhas para CSV com separador `;` e números em vírgula decimal (pt-PT). */
export function toCsv(rows: CsvCell[][]): string {
  lastRows = rows
  return rows.map((row) => row.map(formatCell).join(SEPARATOR)).join('\r\n')
}

/** Nome de ficheiro estável: `{prefixo}_{empresa}_{AAAA-MM-DD}.csv`. */
export function csvFilename(prefix: string, company: string): string {
  const slug = company
    .toLowerCase()
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
  const today = new Date().toISOString().slice(0, 10)
  return `${prefix}_${slug || 'empresa'}_${today}.csv`
}

// --- Excel e PDF (F4-04) -----------------------------------------------------------
// Todos os ecrãs exportam com `downloadCsv(csvFilename(…), toCsv(linhas))`. Para lhes
// dar Excel e PDF sem reescrever cada um, o botão de exportação (`ExportButtons`) anuncia
// aqui o formato pedido antes de chamar a função de exportação do ecrã; `toCsv` guarda as
// linhas tal como o ecrã as montou (com os números ainda números) e `downloadCsv`, em vez
// de descarregar o CSV, envia-as ao servidor, que devolve o ficheiro.

export type ExportFormat = 'csv' | 'xlsx' | 'pdf'
type PendingExport = { format: Exclude<ExportFormat, 'csv'>; url: string; title: string; settle: (error?: Error) => void }

let lastRows: CsvCell[][] | null = null
let pending: PendingExport | null = null
let capture: ((rows: CsvCell[][] | null) => void) | null = null

/**
 * Pede à função de exportação do ecrã as linhas que mostra, sem descarregar nada: serve o «Enviar por email»,
 * que manda as linhas ao servidor para ele gerar o anexo. Devolve `null` se o ecrã não tinha nada para exportar.
 */
export function captureRows(run: () => unknown): Promise<CsvCell[][] | null> {
  return new Promise((resolve) => {
    capture = resolve
    lastRows = null
    try { void run() } finally {
      // Se a função do ecrã não chamou `downloadCsv` (nada para exportar), termina sem linhas.
      window.setTimeout(() => { if (capture === resolve) { capture = null; resolve(null) } }, 0)
    }
  })
}

/** Anuncia o formato do próximo `downloadCsv`. A promessa termina com o descarregamento. */
export function beginExport(format: Exclude<ExportFormat, 'csv'>, url: string, title: string): Promise<void> {
  pending?.settle()
  return new Promise<void>((resolve, reject) => {
    pending = { format, url, title, settle: (error) => (error ? reject(error) : resolve()) }
  })
}

/** Desfaz um pedido que a função de exportação do ecrã não chegou a usar (nada para exportar, erro antes). */
export function cancelExport(): void {
  pending?.settle()
  pending = null
}

function saveBlob(filename: string, blob: Blob): void {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = filename
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  URL.revokeObjectURL(url)
}

async function exportOnServer(request: PendingExport, filename: string, rows: CsvCell[][]): Promise<void> {
  const response = await fetch(`${request.url}?format=${request.format}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ title: request.title, rows }),
  })
  if (!response.ok) {
    let message = `Não foi possível exportar (HTTP ${response.status}).`
    try {
      const body = await response.json() as { errors?: Record<string, string[]> }
      message = Object.values(body.errors ?? {}).flat()[0] ?? message
    } catch { /* fica a mensagem genérica */ }
    throw new Error(message)
  }
  saveBlob(filename.replace(/\.csv$/i, `.${request.format}`), await response.blob())
}

/**
 * Descarrega o CSV com BOM UTF-8 para o Excel apresentar os acentos — ou, se foi pedido
 * Excel ou PDF (ver acima), o ficheiro gerado pelo servidor com as mesmas linhas.
 */
export function downloadCsv(filename: string, csv: string): void {
  if (capture) {
    const done = capture
    capture = null
    done(lastRows)
    return
  }

  const request = pending
  pending = null
  if (request && lastRows) {
    void exportOnServer(request, filename, lastRows).then(
      () => request.settle(),
      (error: unknown) => request.settle(error instanceof Error ? error : new Error('Não foi possível exportar.')))
    return
  }

  request?.settle()
  saveBlob(filename, new Blob([`\ufeff${csv}`], { type: 'text/csv;charset=utf-8' }))
}
