import { Banknote, CircleAlert, CircleCheck, Database, Download, FileUp, Link2, Link2Off, Wand2 } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { euro, formatMovementDate, type BankAccountRow, type BankReconciliationReport, type BankStatementImportResult, type BankStatementLineRow } from '../app/demo-data'
import { PeriodShortcuts, useExerciseDateRange } from '../app/periods'
import { usePublishRecord } from '../app/RecordContext'
import { useWorkContext } from '../app/WorkContext'

export function BanksPage() {
  const { activeCompany, source, bankAccounts, importBankStatement, bankStatementLines, autoReconcileBank, reconcileBankLine, unreconcileBankLine, bankReconciliation } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const fiscalYear = activeCompany.settings.defaultFiscalYear
  const { fromDate, setFromDate, toDate, setToDate } = useExerciseDateRange(fiscalYear, companyId)
  const inputRef = useRef<HTMLInputElement>(null)
  const [accounts, setAccounts] = useState<BankAccountRow[] | null>(null)
  const [accountCode, setAccountCode] = useState('')
  const [lines, setLines] = useState<BankStatementLineRow[]>([])
  const [report, setReport] = useState<BankReconciliationReport | null>(null)
  const [pending, setPending] = useState<{ file: File; preview: BankStatementImportResult } | null>(null)
  const [choice, setChoice] = useState<Record<string, string>>({})
  const [notice, setNotice] = useState('')
  const [reload, setReload] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => {
    if (!onDatabase) { setAccounts(null); return }
    const controller = new AbortController()
    void bankAccounts(companyId)
      .then((rows) => {
        if (controller.signal.aborted) return
        setAccounts(rows)
        setAccountCode((current) => (rows.some((row) => row.accountCode === current) ? current : rows.find((row) => row.lineCount > 0)?.accountCode ?? rows[0]?.accountCode ?? ''))
      })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar as contas bancárias.') })
    return () => controller.abort()
  }, [bankAccounts, companyId, onDatabase, reload])

  useEffect(() => {
    if (!onDatabase || !accountCode || !fromDate || !toDate) { setLines([]); setReport(null); return }
    const controller = new AbortController()
    setError('')
    void Promise.all([bankStatementLines(companyId, accountCode, { from: fromDate, to: toDate }), bankReconciliation(companyId, accountCode, toDate)])
      .then(([rows, map]) => { if (!controller.signal.aborted) { setLines(rows); setReport(map) } })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar o extrato.') })
    return () => controller.abort()
  }, [accountCode, bankReconciliation, bankStatementLines, companyId, fromDate, onDatabase, reload, toDate])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Trabalho</p><h1>Bancos</h1><p>Extratos bancários importados, conciliação com os lançamentos e mapa de reconciliação.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Banknote /></span><h2>Requer ligação à base de dados</h2><p>Os extratos ficam guardados por empresa e por conta de depósitos.</p></section>
    </div>
  }

  async function run(action: () => Promise<void>, fallback: string) {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await action()
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : fallback)
    } finally {
      setBusy(false)
    }
  }

  const choose = (files: FileList | null) => {
    const file = files?.[0]
    if (inputRef.current) inputRef.current.value = ''
    if (!file || !accountCode) return
    void run(async () => setPending({ file, preview: await importBankStatement(companyId, accountCode, file, true) }), 'Não foi possível ler o extrato.')
  }

  const confirmImport = () => void run(async () => {
    if (!pending) return
    const result = await importBankStatement(companyId, accountCode, pending.file, false)
    setPending(null)
    setNotice(`Importados ${result.imported} movimentos${result.alreadyExisted > 0 ? `; ${result.alreadyExisted} já existiam` : ''}.`)
    setReload((value) => value + 1)
  }, 'Não foi possível importar o extrato.')

  const autoReconcile = () => void run(async () => {
    const matched = await autoReconcileBank(companyId, accountCode, { from: fromDate, to: toDate })
    setNotice(matched === 0 ? 'Nenhum movimento novo encontrou lançamento com o mesmo valor.' : `${matched} ${matched === 1 ? 'movimento conciliado' : 'movimentos conciliados'} pelo valor e pela data.`)
    setReload((value) => value + 1)
  }, 'Não foi possível conciliar.')

  const reconcile = (line: BankStatementLineRow) => void run(async () => {
    const reference = (choice[line.id] ?? '').trim()
    if (!reference) throw new Error('Escolha ou escreva a referência do lançamento.')
    await reconcileBankLine(companyId, line.id, reference)
    setReload((value) => value + 1)
  }, 'Não foi possível conciliar o movimento.')

  const unreconcile = (line: BankStatementLineRow) => void run(async () => {
    await unreconcileBankLine(companyId, line.id)
    setReload((value) => value + 1)
  }, 'Não foi possível desfazer a conciliação.')

  const account = accounts?.find((row) => row.accountCode === accountCode)
  const candidates = (amount: number) => (report?.bookOnly ?? []).filter((entry) => entry.amount === amount)
  const exportCsv = () => downloadCsv(
    csvFilename(`extrato-${accountCode}-${fromDate}-${toDate}`, activeCompany.legalName),
    toCsv([
      ['Data', 'Data-valor', 'Descrição', 'Referência', 'Montante', 'Saldo', 'Lançamento', 'Conciliação'],
      ...lines.map((line) => [line.bookingDate, line.valueDate ?? '', line.description, line.reference ?? '', line.amount, line.balance ?? '', line.reconciledEntry?.reference ?? '', line.reconciledEntry ? (line.reconciledAutomatically ? 'Automática' : 'Manual') : 'Por conciliar']),
    ]),
  )

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Trabalho</p><h1>Bancos</h1><p>Importe o extrato de cada conta de depósitos — ISO 20022 camt.053 ou o CSV do banco — e concilie cada movimento com o lançamento que lhe corresponde. O mapa de reconciliação explica a diferença entre o saldo do extrato e o contabilístico.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
    {notice && <p className="save-status"><CircleCheck size={15} /> {notice}</p>}

    {accounts && accounts.length === 0 && <section className="panel empty-state"><span className="empty-icon"><Banknote /></span><h2>Sem contas de depósitos</h2><p>Crie no <Link to="/contas">plano de contas</Link> uma subconta de movimento em 11, 12 ou 13 para cada conta bancária.</p></section>}

    {accounts && accounts.length > 0 && <>
      <div className="filterbar">
        <label className="inline-check">Conta <select aria-label="Conta bancária" value={accountCode} onChange={(event) => { setAccountCode(event.target.value); setPending(null) }}>{accounts.map((row) => <option key={row.accountCode} value={row.accountCode}>{row.accountCode} — {row.accountName}{row.unreconciledCount > 0 ? ` (${row.unreconciledCount} por conciliar)` : ''}</option>)}</select></label>
        <label><span className="sr-only">Data inicial</span><input type="date" aria-label="Data inicial" value={fromDate} onChange={(event) => setFromDate(event.target.value)} /></label>
        <label><span className="sr-only">Data final</span><input type="date" aria-label="Data final" value={toDate} onChange={(event) => setToDate(event.target.value)} /></label>
        <PeriodShortcuts fiscalYear={fiscalYear ?? new Date().getUTCFullYear()} onPick={(from, to) => { setFromDate(from); setToDate(to) }} />
      </div>

      <section className="panel">
        <div className="panel-heading"><div><h2>Importar extrato</h2><p>{account?.iban ? `IBAN ${account.iban}. ` : ''}Reimportar o mesmo extrato, ou um que se sobreponha a outro, não duplica os movimentos. Os movimentos pendentes de um camt.053 ficam de fora.</p></div></div>
        <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
          <input ref={inputRef} type="file" accept=".csv,.txt,.xml,text/csv,application/xml,text/xml" className="sr-only" aria-label="Escolher extrato bancário" onChange={(event) => choose(event.target.files)} />
          <button className="button button-secondary" type="button" disabled={busy || !accountCode} onClick={() => inputRef.current?.click()}><FileUp size={16} /> Escolher ficheiro</button>
          {pending && <>
            <span className="inline-check">{pending.file.name} ({pending.preview.format}): {pending.preview.imported} novos, {pending.preview.alreadyExisted} já importados{pending.preview.invalid > 0 ? `, ${pending.preview.invalid} ilegíveis` : ''}{pending.preview.closingBalance !== null ? ` · saldo final ${euro.format(pending.preview.closingBalance)}` : ''}</span>
            <button className="button button-primary" type="button" disabled={busy || pending.preview.imported === 0} onClick={confirmImport}>Importar</button>
          </>}
        </div>
        {pending?.preview.iban && account?.iban && pending.preview.iban !== account.iban && <p className="save-status error"><CircleAlert size={15} /> O ficheiro é do IBAN {pending.preview.iban}, mas esta conta já recebeu extratos do IBAN {account.iban}.</p>}
        {pending?.preview.warnings.map((warning) => <p key={warning} className="save-status"><CircleAlert size={15} /> {warning}</p>)}
        {pending && pending.preview.invalidRows.length > 0 && <p className="save-status"><CircleAlert size={15} /> {pending.preview.invalidRows.slice(0, 5).map((row) => `linha ${row.lineNumber}: ${row.reason}`).join(' · ')}</p>}
      </section>

      {report && <section className="metadata-strip">
        <span><small>Saldo contabilístico em {formatMovementDate(report.date)}</small><strong>{euro.format(report.bookBalance)}</strong></span>
        <span><small>Saldo do extrato</small><strong>{report.statementBalance === null ? '—' : euro.format(report.statementBalance)}</strong><small>{report.statementBalanceDate ? `em ${formatMovementDate(report.statementBalanceDate)}` : 'sem saldo no extrato'}</small></span>
        <span><small>Só no banco</small><strong>{euro.format(report.statementOnlyTotal)}</strong><small>{report.statementOnly.length} movimentos</small></span>
        <span><small>Só na contabilidade</small><strong>{euro.format(report.bookOnlyTotal)}</strong><small>{report.bookOnly.length} lançamentos</small></span>
        <span><small>Diferença por explicar</small><strong>{report.difference === null ? '—' : <span className={`badge ${report.difference === 0 ? 'success' : 'warning'}`}>{euro.format(report.difference)}</span>}</strong></span>
      </section>}

      <section className="panel">
        <div className="panel-heading"><div><h2>Movimentos do extrato</h2><p>{lines.length === 0 ? 'Sem movimentos importados no período.' : `${lines.filter((line) => line.reconciledEntry).length} de ${lines.length} conciliados.`} A conciliação automática junta cada movimento a um lançamento da conta com o mesmo valor, até 7 dias de distância, o mais próximo primeiro.</p></div></div>
        <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
          <button className="button button-primary" type="button" disabled={busy || lines.every((line) => line.reconciledEntry)} onClick={autoReconcile}><Wand2 size={16} /> Conciliar automaticamente</button>
          <button className="button button-secondary" type="button" disabled={lines.length === 0} onClick={exportCsv}><Download size={16} /> Exportar CSV</button>
        </div>
        {lines.length > 0 && <div className="table-wrap"><table>
          <thead><tr><th>Data</th><th>Descrição</th><th className="numeric">Montante</th><th className="numeric">Saldo</th><th>Lançamento</th></tr></thead>
          <tbody>{lines.map((line) => <tr key={line.id}>
            <td>{formatMovementDate(line.bookingDate)}{line.valueDate && line.valueDate !== line.bookingDate ? <small>valor {formatMovementDate(line.valueDate)}</small> : null}</td>
            <td>{line.description}{line.reference ? <small>{line.reference}</small> : null}</td>
            <td className={`numeric strong${line.amount < 0 ? ' negative' : ''}`}>{euro.format(line.amount)}</td>
            <td className="numeric">{line.balance === null ? '' : euro.format(line.balance)}</td>
            <td>{line.reconciledEntry
              ? <span className="inline-check"><Link to={`/movimentos/${line.reconciledEntry.reference}`}>{line.reconciledEntry.reference}</Link><small>{formatMovementDate(line.reconciledEntry.entryDate)} · {line.reconciledEntry.description}{line.reconciledAutomatically ? ' · automática' : ''}</small><button className="icon-button" type="button" aria-label={`Desfazer a conciliação de ${line.description}`} title="Desfazer a conciliação" disabled={busy} onClick={() => unreconcile(line)}><Link2Off size={15} /></button></span>
              : <span className="inline-check">
                <input aria-label={`Lançamento para ${line.description}`} list={`candidatos-${line.id}`} value={choice[line.id] ?? ''} onChange={(event) => setChoice((current) => ({ ...current, [line.id]: event.target.value.toUpperCase() }))} placeholder={candidates(line.amount).length > 0 ? `${candidates(line.amount).length} com o mesmo valor` : 'referência'} />
                <datalist id={`candidatos-${line.id}`}>{candidates(line.amount).map((entry) => <option key={entry.reference} value={entry.reference}>{formatMovementDate(entry.entryDate)} · {entry.description}</option>)}</datalist>
                <button className="icon-button" type="button" aria-label={`Conciliar ${line.description}`} title="Conciliar com este lançamento" disabled={busy || !(choice[line.id] ?? '').trim()} onClick={() => reconcile(line)}><Link2 size={15} /></button>
              </span>}</td>
          </tr>)}</tbody>
        </table></div>}
      </section>

      {report && report.bookOnly.length > 0 && <section className="panel">
        <div className="panel-heading"><div><h2>Só na contabilidade</h2><p>Lançamentos da conta desde o primeiro extrato importado que ainda não passaram no banco — cheques por descontar, transferências em curso — ou que falta conciliar.</p></div></div>
        <div className="table-wrap"><table>
          <thead><tr><th>Data</th><th>Lançamento</th><th>Descrição</th><th className="numeric">Montante</th></tr></thead>
          <tbody>{report.bookOnly.map((entry) => <tr key={entry.reference}>
            <td>{formatMovementDate(entry.entryDate)}</td>
            <td><Link to={`/movimentos/${entry.reference}`}>{entry.reference}</Link><small>{entry.documentReference}</small></td>
            <td>{entry.description}</td>
            <td className="numeric">{euro.format(entry.amount)}</td>
          </tr>)}</tbody>
        </table></div>
      </section>}

      <p className="statement-note">O montante é positivo quando entra dinheiro e negativo quando sai — o mesmo sinal que débito menos crédito na conta. Saldo do extrato = saldo contabilístico + movimentos só no banco − lançamentos só na contabilidade; a diferença por explicar deve ser zero. Um lançamento anulado deixa de contar como conciliado.</p>
    </>}
  </div>
}
