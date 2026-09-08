import { CircleAlert, Printer, Settings2 } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import type { ReportTemplate } from './demo-data'
import { useWorkContext } from './WorkContext'

const printedOn = () => new Intl.DateTimeFormat('pt-PT', { dateStyle: 'long', timeStyle: 'short' }).format(new Date())

/**
 * Cabeçalho de impressão configurável e barra «Imprimir / Configurar impressão»
 * partilhados pelos relatórios fixos. O cabeçalho só aparece na impressão; a
 * barra desaparece na impressão. O modelo é por empresa e por relatório
 * (`reportKey`); sem ligação à base de dados usa-se um cabeçalho por omissão e
 * a gravação fica indisponível.
 */
export function ReportChrome({ reportKey, title, from, to }: {
  reportKey: string
  title: string
  from?: string
  to?: string
}) {
  const { activeCompany, reportTemplate, saveReportTemplate, source } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'

  const fallback = useMemo<ReportTemplate>(() => ({
    reportKey,
    headerLines: [],
    showFirmName: true,
    showCompanyIdentity: true,
    orientation: 'Portrait',
    companyName: activeCompany.legalName,
    companyTaxId: activeCompany.taxRegistrationNumber,
    firmName: '',
  }), [reportKey, activeCompany.legalName, activeCompany.taxRegistrationNumber])

  const [template, setTemplate] = useState<ReportTemplate>(fallback)
  const [open, setOpen] = useState(false)
  const [draft, setDraft] = useState({ headerLines: '', showFirmName: true, showCompanyIdentity: true, orientation: 'Portrait' as 'Portrait' | 'Landscape' })
  const [status, setStatus] = useState('')

  useEffect(() => {
    setTemplate(fallback)
    void reportTemplate(companyId, reportKey).then(setTemplate).catch(() => setTemplate(fallback))
  }, [companyId, reportKey, reportTemplate, fallback])

  useEffect(() => {
    setDraft({
      headerLines: template.headerLines.join('\n'),
      showFirmName: template.showFirmName,
      showCompanyIdentity: template.showCompanyIdentity,
      orientation: template.orientation,
    })
  }, [template])

  async function save() {
    setStatus('')
    try {
      const stored = await saveReportTemplate(companyId, reportKey, {
        headerLines: draft.headerLines.split('\n').map((line) => line.trim()).filter(Boolean).slice(0, 6),
        showFirmName: draft.showFirmName,
        showCompanyIdentity: draft.showCompanyIdentity,
        orientation: draft.orientation,
      })
      setTemplate(stored)
      setStatus('Modelo de impressão guardado.')
    } catch (problem) {
      setStatus(problem instanceof Error ? problem.message : 'Não foi possível guardar.')
    }
  }

  return <>
    {template.orientation === 'Landscape' && <style>{'@page { size: landscape; }'}</style>}

    <div className="print-only report-print-head">
      {template.showFirmName && template.firmName && <p className="report-firm">{template.firmName}</p>}
      {template.showCompanyIdentity && <p className="report-company">{template.companyName} · NIF {template.companyTaxId}</p>}
      <p className="report-print-title">{title}</p>
      {(from || to) && <p className="report-period">{from || '…'} a {to || '…'}</p>}
      {template.headerLines.map((line, index) => <p key={index} className="report-line">{line}</p>)}
      <p className="report-meta">Impresso em {printedOn()}</p>
    </div>

    <section className="panel no-print report-chrome">
      <div className="report-chrome-bar">
        <button className="button button-secondary" type="button" onClick={() => window.print()}><Printer size={15} /> Imprimir / PDF</button>
        <button className="button button-secondary" type="button" aria-expanded={open} onClick={() => setOpen((value) => !value)}><Settings2 size={15} /> Configurar impressão</button>
      </div>
      {open && <div className="report-chrome-panel">
        {!onDatabase && <p className="save-status"><CircleAlert size={15} /> Guardar o modelo requer ligação ao SQL Server.</p>}
        <label className="field-block" style={{ margin: 0 }}><span>Linhas de cabeçalho <small>(uma por linha, até 6)</small></span>
          <textarea rows={3} value={draft.headerLines} onChange={(event) => setDraft((current) => ({ ...current, headerLines: event.target.value }))} placeholder="Ex.: Documento de trabalho" />
        </label>
        <div className="report-chrome-toggles">
          <label><input type="checkbox" checked={draft.showFirmName} onChange={(event) => setDraft((current) => ({ ...current, showFirmName: event.target.checked }))} /> Nome do gabinete</label>
          <label><input type="checkbox" checked={draft.showCompanyIdentity} onChange={(event) => setDraft((current) => ({ ...current, showCompanyIdentity: event.target.checked }))} /> Identificação da empresa</label>
          <label>Orientação
            <select value={draft.orientation} onChange={(event) => setDraft((current) => ({ ...current, orientation: event.target.value as 'Portrait' | 'Landscape' }))}>
              <option value="Portrait">Retrato</option>
              <option value="Landscape">Paisagem</option>
            </select>
          </label>
        </div>
        {status && <p className="save-status">{status}</p>}
        <button className="button button-secondary" type="button" disabled={!onDatabase} onClick={() => void save()}>Guardar modelo</button>
      </div>}
    </section>
  </>
}
