import { Download } from 'lucide-react'
import { useState } from 'react'
import { beginExport, cancelExport, type ExportFormat } from './csv'
import { useWorkContext } from './WorkContext'

/**
 * Os botões de exportação de uma listagem: CSV (no browser), Excel e PDF (gerados no
 * servidor, F4-04). `run` é a função de exportação do ecrã, a mesma que fazia o CSV.
 */
export function ExportButtons({ run, disabled = false, label = 'Exportar' }: {
  run: () => unknown
  disabled?: boolean
  label?: string
}) {
  const { activeCompany, source, user } = useWorkContext()
  const [busy, setBusy] = useState<ExportFormat | null>(null)
  const [error, setError] = useState('')

  async function start(format: ExportFormat) {
    setError('')
    if (format === 'csv') {
      run()
      return
    }

    setBusy(format)
    // O título do ficheiro é o do ecrã.
    const title = document.querySelector('.content-area h1')?.textContent?.trim() || 'Listagem'
    const done = beginExport(format, `/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/exports/table`, title)
    try {
      await run()
      // Se a função do ecrã não chegou a exportar (nada para exportar), desfaz o pedido.
      window.setTimeout(cancelExport, 0)
      await done
    } catch (problem) {
      cancelExport()
      setError(problem instanceof Error ? problem.message : 'Não foi possível exportar.')
    } finally {
      setBusy(null)
    }
  }

  // Excel e PDF precisam do servidor; em modo de demonstração só há CSV.
  const server = source === 'database'
  return <span className="export-buttons" role="group" aria-label={label}>
    <span className="export-buttons-label"><Download size={15} /> {label}</span>
    <button className="button button-secondary" type="button" disabled={disabled || busy !== null} onClick={() => void start('csv')}>CSV</button>
    {server && <button className="button button-secondary" type="button" disabled={disabled || busy !== null} onClick={() => void start('xlsx')}>{busy === 'xlsx' ? 'A gerar…' : 'Excel'}</button>}
    {server && <button className="button button-secondary" type="button" disabled={disabled || busy !== null} onClick={() => void start('pdf')}>{busy === 'pdf' ? 'A gerar…' : 'PDF'}</button>}
    {error && <span className="save-status error" role="alert">{error}</span>}
  </span>
}
