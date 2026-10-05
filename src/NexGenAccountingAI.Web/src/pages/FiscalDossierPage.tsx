import { CircleAlert, CircleCheck, Database, FolderCheck, Printer } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { type FiscalDossier } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

const statusLabels: Record<string, string> = { pendente: 'Por preparar', pronto: 'Pronto', 'nao-aplicavel': 'Não aplicável' }
const statusBadges: Record<string, string> = { pendente: 'warning', pronto: 'success', 'nao-aplicavel': 'neutral' }
const supportLabels: Record<string, string> = {
  aplicacao: 'Mapa na aplicação',
  parcial: 'Mapa de apoio na aplicação (não é o modelo oficial)',
  externo: 'Preparado fora da aplicação',
}

export function FiscalDossierPage() {
  const { activeCompany, source, fiscalDossier, setFiscalDossierItem } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const defaultYear = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const [year, setYear] = useState(defaultYear)
  const [dossier, setDossier] = useState<FiscalDossier | null>(null)
  const [notes, setNotes] = useState<Record<number, string>>({})
  const [saving, setSaving] = useState<number | null>(null)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => { setYear(defaultYear) }, [defaultYear, companyId])

  function load(value: FiscalDossier) {
    setDossier(value)
    setNotes(Object.fromEntries(value.elements.map((element) => [element.number, element.note ?? ''])))
  }

  useEffect(() => {
    if (!onDatabase) { setDossier(null); return }
    const controller = new AbortController()
    setError('')
    void fiscalDossier(companyId, year)
      .then((value) => { if (!controller.signal.aborted) load(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setDossier(null); setError(problem instanceof Error ? problem.message : 'Não foi possível carregar o dossier fiscal.') } })
    return () => controller.abort()
  }, [fiscalDossier, companyId, onDatabase, year])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Dossier fiscal</h1><p>O índice do processo de documentação fiscal de cada exercício, com o estado de cada elemento.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><FolderCheck /></span><h2>Requer ligação à base de dados</h2><p>O dossier é acompanhado por empresa e por exercício.</p></section>
    </div>
  }

  async function save(element: number, status: string) {
    setSaving(element)
    setError('')
    try {
      load(await setFiscalDossierItem(companyId, year, element, { status, note: notes[element]?.trim() || null }))
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível guardar o elemento.')
    } finally {
      setSaving(null)
    }
  }

  const elements = dossier?.elements ?? []

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Dossier fiscal</h1><p>Os doze elementos do processo de documentação fiscal (artigo 130.º do CIRC): o que a contabilidade do exercício mostra sobre cada um, onde a aplicação ajuda, e o estado que lhe der. Os mapas de modelo oficial 30, 31 e 32 estão em <Link to="/mapas-dossier">Mapas do dossier fiscal</Link>.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}

    <div className="filterbar">
      <label className="inline-check">Exercício <input type="number" aria-label="Exercício" min={2000} max={9999} value={year} onChange={(event) => { const next = Number(event.target.value); if (next >= 2000 && next <= 9999) setYear(next) }} /></label>
      <button className="button button-secondary" type="button" onClick={() => window.print()} disabled={!dossier}><Printer size={15} /> Imprimir / PDF</button>
    </div>

    {dossier && <>
      {dossier.pending === 0
        ? <p className="save-status success"><CircleCheck size={15} /> Todos os elementos de {year} estão prontos ou não se aplicam.</p>
        : <p className="save-status error"><CircleAlert size={15} /> {dossier.pending} elemento(s) de {year} por preparar.</p>}

      <section className="panel">
        <div className="table-wrap"><table>
          <thead><tr><th>N.º</th><th>Elemento</th><th>Estado</th><th>Nota</th><th /></tr></thead>
          <tbody>{elements.map((element) => <tr key={element.number}>
            <td className="primary-cell">{element.number}</td>
            <td>
              <strong>{element.title}</strong>
              <br /><small>{supportLabels[element.support] ?? element.support}{element.link && <> — <Link to={element.link}>{element.linkLabel}</Link></>}</small>
              {element.evidence && <><br /><small>{element.evidence}</small></>}
            </td>
            <td><span className={`badge ${statusBadges[element.status] ?? 'neutral'}`}>{statusLabels[element.status] ?? element.status}</span></td>
            <td><input aria-label={`Nota do elemento ${element.number}`} value={notes[element.number] ?? ''} maxLength={500} placeholder="ex.: onde está arquivado" onChange={(event) => setNotes((current) => ({ ...current, [element.number]: event.target.value }))} /></td>
            <td>
              <div className="period-shortcuts" role="group" aria-label={`Estado do elemento ${element.number}`}>
                {(['pronto', 'nao-aplicavel', 'pendente'] as const).map((status) => <button key={status} type="button" className={`period-chip${element.status === status ? ' active' : ''}`} disabled={saving === element.number} onClick={() => void save(element.number, status)}>{statusLabels[status]}</button>)}
              </div>
            </td>
          </tr>)}</tbody>
        </table></div>
      </section>

      <p className="statement-note">{dossier.disclaimer}</p>
      <p className="statement-note">Fonte da lista: {dossier.source}</p>
    </>}
  </div>
}
