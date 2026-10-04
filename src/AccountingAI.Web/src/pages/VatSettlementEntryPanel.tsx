import { CircleAlert, CircleCheck } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { euro, type VatSettlementEntryPreview } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

/** A proposta do lançamento de apuramento do IVA do período e o botão que o gera. */
export function VatSettlementEntryPanel({ companyId, from, to }: { companyId: string; from: string; to: string }) {
  const { vatSettlementEntry, postVatSettlementEntry } = useWorkContext()
  const [accounts, setAccounts] = useState({ settlement: '2435', payable: '2436', recoverable: '2437' })
  const [journal, setJournal] = useState('')
  const [preview, setPreview] = useState<VatSettlementEntryPreview | null>(null)
  const [posting, setPosting] = useState(false)
  const [posted, setPosted] = useState('')
  const [error, setError] = useState('')
  const [reload, setReload] = useState(0)

  useEffect(() => { setPosted('') }, [companyId, from, to])

  useEffect(() => {
    if (!from || !to) { setPreview(null); return }
    const controller = new AbortController()
    setError('')
    void vatSettlementEntry(companyId, { from, to, ...accounts })
      .then((value) => { if (!controller.signal.aborted) setPreview(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) { setPreview(null); setError(problem instanceof Error ? problem.message : 'Não foi possível preparar o lançamento de apuramento.') } })
    return () => controller.abort()
  }, [vatSettlementEntry, companyId, from, to, accounts, reload])

  async function post() {
    setPosting(true)
    setError('')
    try {
      const movement = await postVatSettlementEntry(companyId, { from, to, ...accounts, journal: journal.trim() })
      setPosted(movement.reference)
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível gerar o lançamento de apuramento.')
    } finally {
      setPosting(false)
    }
  }

  const account = (key: keyof typeof accounts, label: string) => <label className="inline-check">{label} <input aria-label={label} value={accounts[key]} onChange={(event) => setAccounts({ ...accounts, [key]: event.target.value.trim() })} /></label>

  return <section className="panel settings-panel">
    <div className="panel-heading"><div><h2>Lançamento de apuramento do IVA</h2><p>Salda as contas de IVA dedutível, liquidado e regularizações do período por contrapartida da conta de apuramento, junta o IVA a recuperar que vinha de trás e leva o resultado ao IVA a pagar ou a recuperar. Fica por conferir nos <Link to="/movimentos">movimentos</Link>.</p></div></div>

    <div className="filterbar">
      {account('settlement', 'Conta de apuramento')}
      {account('payable', 'Conta do IVA a pagar')}
      {account('recoverable', 'Conta do IVA a recuperar')}
      <label className="inline-check">Diário <input aria-label="Diário do apuramento" value={journal} onChange={(event) => setJournal(event.target.value.toUpperCase())} placeholder="código do diário" /></label>
      <button className="button button-primary" type="button" disabled={!preview || preview.blocker !== null || posting || journal.trim() === ''} onClick={() => void post()}>{posting ? 'A lançar…' : 'Gerar lançamento'}</button>
    </div>

    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
    {posted && <p className="save-status success"><CircleCheck size={15} /> Lançamento {posted} criado, por conferir.</p>}
    {preview?.blocker && !posted && <p className="save-status error"><CircleAlert size={15} /> {preview.blocker}</p>}

    {preview && preview.blocker === null && <>
      <div className="table-wrap"><table>
        <thead><tr><th>Conta</th><th className="numeric">Débito</th><th className="numeric">Crédito</th></tr></thead>
        <tbody>
          {preview.lines.map((line, index) => <tr key={`${line.accountCode}-${index}`}>
            <td><Link to={`/contas/${line.accountCode}`}>{line.accountCode}</Link> {line.accountName}</td>
            <td className="numeric">{line.debit === 0 ? '' : euro.format(line.debit)}</td>
            <td className="numeric">{line.credit === 0 ? '' : euro.format(line.credit)}</td>
          </tr>)}
          <tr className="subtotal-row"><td>{preview.payable > 0 ? `IVA a pagar: ${euro.format(preview.payable)}` : `IVA a recuperar: ${euro.format(preview.recoverable)}`}{preview.priorCredit > 0 ? ` (inclui ${euro.format(preview.priorCredit)} a recuperar de períodos anteriores)` : ''}</td><td className="numeric">{euro.format(preview.lines.reduce((sum, line) => sum + line.debit, 0))}</td><td className="numeric">{euro.format(preview.lines.reduce((sum, line) => sum + line.credit, 0))}</td></tr>
        </tbody>
      </table></div>
    </>}
  </section>
}
