import { CircleCheck, Database, EyeOff, RotateCcw, ScanSearch, TriangleAlert } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { signalLoadFailure } from '../app/loadFailure'
import { useWorkContext } from '../app/WorkContext'

type Finding = {
  key: string
  rule: string
  severity: 'high' | 'medium' | 'low'
  title: string
  detail: string
  entryReferences: string[]
  accountCode: string | null
  partyName: string | null
  amount: number | null
  date: string | null
  dismissed: boolean
  dismissReason: string | null
}
type RuleSummary = { rule: string; label: string; description: string; count: number }
type Report = { from: string; to: string; findings: Finding[]; rules: RuleSummary[]; truncated: boolean }

const SEVERITY_LABELS: Record<Finding['severity'], string> = { high: 'Alta', medium: 'Média', low: 'Baixa' }
const amountFormat = new Intl.NumberFormat('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2, useGrouping: 'always' })

/**
 * A deteção de anomalias (F4F-01): o que nos lançamentos de um período merece uma segunda vista
 * antes de fechar. Cada achado diz porquê e leva ao lançamento; pode dispensar-se com uma razão.
 */
export function AnomaliesPage() {
  const { activeCompany, source, user } = useWorkContext()
  const year = activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear()
  const [from, setFrom] = useState(`${year}-01-01`)
  const [to, setTo] = useState(`${year}-12-31`)
  const [showDismissed, setShowDismissed] = useState(false)
  const [report, setReport] = useState<Report | null>(null)
  const [running, setRunning] = useState(false)
  const [error, setError] = useState('')
  const [dismissing, setDismissing] = useState<{ key: string; reason: string } | null>(null)
  const root = `/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/anomalies`

  useEffect(() => { setFrom(`${year}-01-01`); setTo(`${year}-12-31`); setReport(null) }, [year, activeCompany.companyId])

  const scan = useCallback(async () => {
    setRunning(true); setError('')
    try {
      const response = await fetch(`${root}?${new URLSearchParams({ from, to, includeDismissed: String(showDismissed) })}`)
      if (!response.ok) {
        const problem = await response.json().catch(() => null) as { errors?: Record<string, string[]> } | null
        setError(Object.values(problem?.errors ?? {}).flat()[0] ?? 'Não foi possível procurar anomalias.')
        signalLoadFailure()
        return
      }
      setReport(await response.json() as Report)
    } finally {
      setRunning(false)
    }
  }, [root, from, to, showDismissed])

  async function dismiss() {
    if (!dismissing) return
    const response = await fetch(`${root}/dismiss`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(dismissing) })
    if (!response.ok) { setError('Não foi possível dispensar o achado.'); return }
    setDismissing(null)
    await scan()
  }

  async function restore(key: string) {
    const response = await fetch(`${root}/restore`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ key }) })
    if (!response.ok) { setError('Não foi possível repor o achado.'); return }
    await scan()
  }

  if (source !== 'database') {
    return <div className="page-stack"><header className="page-heading"><div><p className="eyebrow">Gestão</p><h1>Anomalias</h1><p>A deteção de anomalias precisa da base de dados; em demonstração não está disponível.</p></div></header></div>
  }

  const total = report?.rules.reduce((sum, rule) => sum + rule.count, 0) ?? 0
  return <div className="page-stack">
    <header className="page-heading detail-heading">
      <div><p className="eyebrow">Gestão</p><h1>Anomalias</h1><p>O que nos lançamentos do período merece uma segunda vista antes de fechar: duplicados, documentos repetidos, valores fora do habitual, linhas sem terceiro e saldos com o sinal trocado. Cada achado diz porquê; nada se altera sozinho.</p></div>
      <span className="connection-badge database"><Database size={15} />Dados da BD</span>
    </header>
    {error && <p className="save-status error" role="alert"><TriangleAlert size={15} /> {error}</p>}

    <section className="panel">
      <div className="filterbar">
        <label className="inline-check">De <input type="date" aria-label="Data inicial" value={from} onChange={(event) => setFrom(event.target.value)} /></label>
        <label className="inline-check">Até <input type="date" aria-label="Data final" value={to} onChange={(event) => setTo(event.target.value)} /></label>
        <label className="inline-check"><input type="checkbox" checked={showDismissed} onChange={(event) => setShowDismissed(event.target.checked)} /> Mostrar os dispensados</label>
        <button type="button" className="button button-primary" disabled={running || !from || !to} onClick={() => void scan()}><ScanSearch size={15} /> {running ? 'A procurar…' : 'Procurar anomalias'}</button>
      </div>
      {!report && !running && <p className="empty-state">Escolha o período e carregue em «Procurar anomalias».</p>}
    </section>

    {report && <>
      <section className="metadata-strip" aria-label="Resumo">
        {report.rules.map((rule) => <span key={rule.rule} title={rule.description}><small>{rule.label}</small><strong>{rule.count}</strong></span>)}
      </section>
      {report.truncated && <p className="save-status"><TriangleAlert size={15} /> Há mais achados do que os mostrados: reduza o período.</p>}
      {report.findings.length === 0 && <p className="save-status success"><CircleCheck size={15} /> Nada a assinalar neste período.</p>}
      {report.rules.map((rule) => {
        const items = report.findings.filter((finding) => finding.rule === rule.rule)
        if (items.length === 0) return null
        return <section key={rule.rule} className="panel" aria-label={rule.label}>
          <div className="panel-heading"><div><h2>{rule.label} <small>({items.filter((item) => !item.dismissed).length})</small></h2><p>{rule.description}</p></div></div>
          <div className="table-wrap"><table>
            <thead><tr><th>Gravidade</th><th>Achado</th><th>Lançamentos</th><th className="numeric">Valor</th><th><span className="sr-only">Ações</span></th></tr></thead>
            <tbody>{items.map((finding) => <tr key={finding.key} className={finding.dismissed ? 'muted-row' : ''}>
              <td><span className={`badge ${finding.severity === 'high' ? 'danger' : finding.severity === 'medium' ? 'warning' : ''}`}>{SEVERITY_LABELS[finding.severity]}</span></td>
              <td><strong>{finding.title}</strong><div><small>{finding.detail}</small></div>
                {finding.dismissed && <div><small>Dispensado{finding.dismissReason ? `: ${finding.dismissReason}` : ''}</small></div>}
              </td>
              <td>{finding.entryReferences.length === 0 ? '—' : finding.entryReferences.map((reference, index) => <span key={reference}>{index > 0 && ', '}<Link to={`/movimentos/${encodeURIComponent(reference)}`}>{reference}</Link></span>)}</td>
              <td className="numeric">{finding.amount === null ? '' : amountFormat.format(finding.amount)}</td>
              <td className="numeric">
                {finding.dismissed
                  ? <button type="button" className="icon-button" aria-label={`Repor ${finding.title}`} onClick={() => void restore(finding.key)}><RotateCcw size={15} /></button>
                  : <button type="button" className="icon-button" aria-label={`Dispensar ${finding.title}`} onClick={() => setDismissing({ key: finding.key, reason: '' })}><EyeOff size={15} /></button>}
              </td>
            </tr>)}</tbody>
          </table></div>
        </section>
      })}
      <p className="statement-note">{total} achado(s) por tratar. As regras são determinísticas e não usam IA: são pistas para conferir, não erros confirmados.</p>
    </>}

    {dismissing && <section className="panel" aria-label="Dispensar achado">
      <div className="panel-heading"><h2>Dispensar o achado</h2></div>
      <label className="inline-check" style={{ display: 'block' }}>Porquê? <input aria-label="Razão para dispensar" maxLength={300} style={{ width: '70%' }} value={dismissing.reason} onChange={(event) => setDismissing({ ...dismissing, reason: event.target.value })} placeholder="ex.: são mesmo duas vendas iguais" /></label>
      <p className="statement-note">O achado deixa de aparecer enquanto os dados forem os mesmos; pode repô-lo em «Mostrar os dispensados».</p>
      <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
        <button type="button" className="button button-primary" onClick={() => void dismiss()}>Dispensar</button>
        <button type="button" className="button button-secondary" onClick={() => setDismissing(null)}>Cancelar</button>
      </div>
    </section>}
  </div>
}
