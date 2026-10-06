import { CircleAlert, CircleCheck, Database, FileDown, FileText, Printer } from 'lucide-react'
import { Fragment, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { ExportButtons } from '../app/ExportButtons'
import { euro, type IesAnnexA } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { problemMessage, useWorkContext } from '../app/WorkContext'

const normLabels: Record<string, string> = { S: 'NCRF (S)', P: 'NCRF-PE (P)', M: 'NC-ME (M)', N: 'NIC (N)' }
const quadros = [
  { key: 'Q03', title: 'Quadro 03 — Demonstração dos resultados por naturezas', note: 'Período N. Os gastos vão com valor positivo: o impresso subtrai-os.' },
  { key: 'Q04', title: 'Quadro 04 — Balanço', note: 'Período N, a 31 de dezembro.' },
]

export function IesPage() {
  const { activeCompany, source, user, iesAnnexA } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const defaultYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const [year, setYear] = useState(defaultYear)
  const [annex, setAnnex] = useState<IesAnnexA | null>(null)
  const [xmlBusy, setXmlBusy] = useState(false)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => { setYear(defaultYear) }, [defaultYear, companyId])

  useEffect(() => {
    if (!onDatabase) { setAnnex(null); return }
    const controller = new AbortController()
    setError('')
    void iesAnnexA(companyId, year)
      .then((value) => { if (!controller.signal.aborted) setAnnex(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setAnnex(null); setError(problem instanceof Error ? problem.message : 'Não foi possível preparar o Anexo A da IES.') } })
    return () => controller.abort()
  }, [iesAnnexA, companyId, onDatabase, year])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>IES — Anexo A</h1><p>A demonstração dos resultados e o balanço pelos campos do Anexo A da Informação Empresarial Simplificada.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><FileText /></span><h2>Requer ligação à base de dados</h2><p>O anexo é preenchido a partir das demonstrações financeiras da empresa.</p></section>
    </div>
  }

  /** O ficheiro XML no formato da AT (esquema «2027 e seguintes»), para entregar no Portal das Finanças. */
  async function downloadXml() {
    setXmlBusy(true); setError('')
    try {
      const response = await fetch(`/api/v1/development/users/${user.id}/companies/${companyId}/ies/xml?year=${year}`)
      if (!response.ok) throw new Error(await problemMessage(response))
      const name = /filename="?([^";]+)/.exec(response.headers.get('Content-Disposition') ?? '')?.[1] ?? `ies-${year}.xml`
      const url = URL.createObjectURL(await response.blob())
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = name
      document.body.appendChild(anchor)
      anchor.click()
      anchor.remove()
      URL.revokeObjectURL(url)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível gerar o ficheiro.')
    } finally {
      setXmlBusy(false)
    }
  }

  const fields = annex?.fields ?? []
  const value = (code: string) => fields.find((field) => field.code === code)?.amount ?? 0
  const exportCsv = () => downloadCsv(
    csvFilename(`ies-anexo-a-${year}`, activeCompany.legalName),
    toCsv([['Quadro', 'Campo', 'Secção', 'Descrição', 'Período N', 'Origem'], ...fields.map((field) => [field.quadro, field.code, field.section, field.label, field.amount, field.computed ? 'soma' : 'rubrica'])]),
  )

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>IES — Anexo A</h1><p>Os quadros 03 e 04 do Anexo A, preenchidos com as rubricas das <Link to="/demonstracoes">demonstrações financeiras</Link>: cada rubrica vai para o campo do impresso com a mesma designação.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    {error && <p className="save-status error">{error}</p>}

    <div className="filterbar">
      <label className="inline-check">Exercício <input type="number" aria-label="Exercício" min={2000} max={9999} value={year} onChange={(event) => { const next = Number(event.target.value); if (next >= 2000 && next <= 9999) setYear(next) }} /></label>
      <ExportButtons run={exportCsv} disabled={fields.length === 0} />
      <button className="button button-secondary" type="button" onClick={() => void downloadXml()} disabled={!annex || xmlBusy}><FileDown size={15} /> {xmlBusy ? 'A gerar…' : 'Ficheiro XML (AT)'}</button>
      <button className="button button-secondary" type="button" onClick={() => window.print()} disabled={!annex}><Printer size={15} /> Imprimir / PDF</button>
    </div>

    {annex && <>
      <section className="metadata-strip">
        <span><small>Normativo (quadro 02-A)</small><strong>{normLabels[annex.norm] ?? annex.norm}</strong></span>
        <span><small>Resultado líquido (A00026)</small><strong>{euro.format(value('A00026'))}</strong></span>
        <span><small>Total do ativo (A00126)</small><strong>{euro.format(value('A00126'))}</strong></span>
        <span><small>Capital próprio e passivo (A00158)</small><strong>{euro.format(value('A00158'))}</strong></span>
      </section>

      {annex.warnings.length === 0
        ? <p className="save-status success"><CircleCheck size={15} /> O balanço fecha e o resultado líquido é o mesmo nos dois quadros.</p>
        : <section className="panel">
          <div className="panel-heading"><div><h2>A corrigir antes de preencher a IES</h2></div></div>
          <ul className="warning-list">{annex.warnings.map((warning) => <li key={warning}><CircleAlert size={15} /> {warning}</li>)}</ul>
        </section>}

      {quadros.map((quadro) => {
        const rows = fields.filter((field) => field.quadro === quadro.key)
        return <section key={quadro.key} className="panel">
          <div className="panel-heading"><div><h2>{quadro.title}</h2><p>{quadro.note} Só aparecem os campos com valor.</p></div></div>
          <div className="table-wrap"><table>
            <thead><tr><th>Campo</th><th>Descrição</th><th className="numeric">Período N</th></tr></thead>
            <tbody>{rows.map((field, index) => <Fragment key={field.code}>
              {field.section !== '' && (index === 0 || rows[index - 1].section !== field.section) && <tr className="analyses-subtotal"><td colSpan={3}>{field.section}</td></tr>}
              <tr className={field.computed ? 'subtotal-row' : undefined}>
                <td className="primary-cell">{field.code}</td>
                <td>{field.label}</td>
                <td className="numeric">{euro.format(field.amount)}</td>
              </tr>
            </Fragment>)}</tbody>
          </table></div>
        </section>
      })}

      <p className="statement-note">{annex.disclaimer}</p>
      <p className="statement-note">Fonte dos campos: {annex.source}</p>
    </>}
  </div>
}
