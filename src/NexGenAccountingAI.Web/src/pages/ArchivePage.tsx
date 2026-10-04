import { Archive, CircleAlert, CircleCheck, Database, FileUp, QrCode, Search, Trash2 } from 'lucide-react'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { euro, formatMovementDate, type DocumentAttachmentRow, type InvoiceQrLookup } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { canScanQrInBrowser, readInvoiceQr } from '../app/qrScan'
import { useWorkContext } from '../app/WorkContext'

const size = (bytes: number) => (bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toLocaleString('pt-PT', { maximumFractionDigits: 1 })} MB`)

export function ArchivePage() {
  const { activeCompany, source, attachments, uploadAttachment, updateAttachment, deleteAttachment, attachmentUrl, lookupInvoiceQr } = useWorkContext()
  const companyId = activeCompany.companyId
  const onDatabase = source === 'database'
  const inputRef = useRef<HTMLInputElement>(null)
  const [rows, setRows] = useState<DocumentAttachmentRow[]>([])
  const [unlinkedOnly, setUnlinkedOnly] = useState(true)
  const [search, setSearch] = useState('')
  const [qrText, setQrText] = useState('')
  const [lookup, setLookup] = useState<InvoiceQrLookup | null>(null)
  const [references, setReferences] = useState<Record<string, string>>({})
  const [reload, setReload] = useState(0)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState('')
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => {
    if (!onDatabase) { setRows([]); return }
    const controller = new AbortController()
    void attachments(companyId, { unlinked: unlinkedOnly, search: search.trim() || undefined })
      .then((value) => { if (!controller.signal.aborted) setRows(value) })
      .catch((problem: unknown) => { if (!controller.signal.aborted) setError(problem instanceof Error ? problem.message : 'Não foi possível carregar o arquivo.') })
    return () => controller.abort()
  }, [attachments, companyId, onDatabase, reload, search, unlinkedOnly])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Trabalho</p><h1>Arquivo digital</h1><p>Os documentos da empresa — faturas, recibos, extratos — ligados aos lançamentos.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><Archive /></span><h2>Requer ligação à base de dados</h2><p>Os documentos ficam guardados por empresa, com a prova de integridade de cada um.</p></section>
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

  const upload = (files: FileList | null) => {
    const list = [...(files ?? [])]
    if (inputRef.current) inputRef.current.value = ''
    if (list.length === 0) return
    void run(async () => {
      const failures: string[] = []
      let withQr = 0
      for (const file of list) {
        try {
          const qr = await readInvoiceQr(file)
          if (qr) withQr++
          await uploadAttachment(companyId, file, { qr })
        } catch (problem) {
          failures.push(`${file.name}: ${problem instanceof Error ? problem.message : 'falhou'}`)
        }
      }

      const done = list.length - failures.length
      setNotice(`${done} ${done === 1 ? 'documento arquivado' : 'documentos arquivados'}${withQr > 0 ? `, ${withQr} com o código QR lido` : ''}.`)
      if (failures.length > 0) setError(failures.join(' · '))
      setReload((value) => value + 1)
    }, 'Não foi possível arquivar.')
  }

  const readQr = (event: FormEvent) => {
    event.preventDefault()
    void run(async () => setLookup(await lookupInvoiceQr(companyId, qrText.trim())), 'Não foi possível ler o código QR.')
  }

  const save = (row: DocumentAttachmentRow, changes: Partial<{ entryReference: string | null; qrText: string | null }>) => void run(async () => {
    await updateAttachment(companyId, row.id, {
      entryReference: changes.entryReference !== undefined ? changes.entryReference : row.entryReference,
      notes: row.notes,
      qrText: changes.qrText ?? null,
    })
    setReload((value) => value + 1)
  }, 'Não foi possível guardar o documento.')

  const remove = (row: DocumentAttachmentRow) => void run(async () => {
    await deleteAttachment(companyId, row.id)
    setReload((value) => value + 1)
  }, 'Não foi possível apagar o documento.')

  // O documento arquivado do código QR lido, para o ligar ao lançamento encontrado.
  const scannedAttachment = lookup ? rows.find((row) => lookup.attachmentIds.includes(row.id)) : undefined

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Trabalho</p><h1>Arquivo digital</h1><p>Arquive o PDF ou a imagem de cada documento e ligue-o ao lançamento. Cada ficheiro guarda a impressão digital (SHA-256) do conteúdo — o mesmo ficheiro não entra duas vezes — e um documento de um lançamento conferido não se apaga.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
    {notice && <p className="save-status"><CircleCheck size={15} /> {notice}</p>}

    <section className="panel">
      <div className="panel-heading"><div><h2>Arquivar</h2><p>PDF, PNG, JPEG, WebP, TIFF ou XML, até 15 MB cada. {canScanQrInBrowser() ? 'Nas imagens, o código QR da fatura é lido automaticamente.' : 'Este navegador não lê códigos QR em imagens: use o leitor de códigos abaixo.'}</p></div></div>
      <div className="settings-actions" style={{ justifyContent: 'flex-start' }}>
        <input ref={inputRef} type="file" multiple accept=".pdf,.png,.jpg,.jpeg,.webp,.tif,.tiff,.xml" className="sr-only" aria-label="Escolher documentos" onChange={(event) => upload(event.target.files)} />
        <button className="button button-primary" type="button" disabled={busy} onClick={() => inputRef.current?.click()}><FileUp size={16} /> Escolher documentos</button>
      </div>
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2>Ler o código QR de uma fatura</h2><p>Aponte o leitor de códigos ao QR da fatura (ou cole o texto) e confirme: a aplicação procura a fatura no e-Fatura, o documento de venda, os lançamentos com o mesmo número e os documentos já arquivados.</p></div></div>
      <form className="filterbar" onSubmit={readQr}>
        <label className="inline-check" style={{ flex: 1 }}><QrCode size={16} /> <input aria-label="Texto do código QR" style={{ flex: 1 }} value={qrText} onChange={(event) => setQrText(event.target.value)} placeholder="A:123456789*B:…*G:FT A/1*…*O:123.00" /></label>
        <button className="button button-secondary" type="submit" disabled={busy || qrText.trim() === ''}>Procurar</button>
      </form>
      {lookup && <div className="metadata-strip">
        <span><small>{lookup.isSale ? 'Venda da empresa' : `Emitente ${lookup.qr.issuerTaxId}`}</small><strong>{lookup.qr.documentNo}</strong><small>{lookup.qr.documentDate ? formatMovementDate(lookup.qr.documentDate) : 'sem data'}{lookup.qr.atcud ? ` · ATCUD ${lookup.qr.atcud}` : ''}</small></span>
        <span><small>Total</small><strong>{euro.format(lookup.qr.grossTotal)}</strong><small>impostos {euro.format(lookup.qr.taxTotal)}</small></span>
        <span><small>{lookup.isSale ? 'Documento de venda importado' : 'Fatura no e-Fatura'}</small><strong>{(lookup.isSale ? lookup.salesDocumentId : lookup.eFaturaPurchaseId) ? 'Sim' : 'Não'}</strong>{!lookup.isSale && !lookup.eFaturaPurchaseId && <small><Link to="/compras-efatura">importar o CSV</Link></small>}</span>
        <span><small>Lançamentos com o mesmo número</small><strong>{lookup.entryReferences.length === 0 ? '—' : lookup.entryReferences.map((reference, index) => <span key={reference}>{index > 0 ? ', ' : ''}<Link to={`/movimentos/${reference}`}>{reference}</Link></span>)}</strong>{lookup.entryReferences.length === 0 && <small><Link to="/lancamentos-sugeridos">ver as sugestões</Link></small>}</span>
        <span><small>No arquivo</small><strong>{lookup.attachmentIds.length === 0 ? 'Não' : 'Sim'}</strong>{scannedAttachment && !scannedAttachment.entryReference && lookup.entryReferences.length === 1 && <button className="text-link" type="button" disabled={busy} onClick={() => save(scannedAttachment, { entryReference: lookup.entryReferences[0] })}>Ligar a {lookup.entryReferences[0]}</button>}</span>
      </div>}
      {lookup?.qr.warnings.map((warning) => <p key={warning} className="save-status"><CircleAlert size={15} /> {warning}</p>)}
    </section>

    <section className="panel">
      <div className="panel-heading"><div><h2>Documentos</h2><p>{unlinkedOnly ? 'Os que ainda não estão ligados a um lançamento.' : 'Os últimos 500 arquivados.'}</p></div></div>
      <div className="filterbar">
        <label className="inline-check"><Search size={15} /> <input aria-label="Procurar no arquivo" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="nome, número, NIF, nota" /></label>
        <label className="inline-check"><input type="checkbox" checked={unlinkedOnly} onChange={(event) => setUnlinkedOnly(event.target.checked)} /> Só sem lançamento</label>
      </div>
      {rows.length === 0
        ? <p className="statement-note">{unlinkedOnly ? 'Todos os documentos estão ligados a um lançamento.' : 'O arquivo está vazio.'}</p>
        : <div className="table-wrap"><table>
          <thead><tr><th>Documento</th><th>Código QR</th><th>Lançamento</th><th /></tr></thead>
          <tbody>{rows.map((row) => <tr key={row.id}>
            <td><a href={attachmentUrl(companyId, row.id)} target="_blank" rel="noreferrer">{row.fileName}</a><small>{size(row.sizeBytes)} · {new Date(row.uploadedAtUtc).toLocaleDateString('pt-PT')} · SHA-256 {row.sha256.slice(0, 12)}…</small></td>
            <td>{row.qrDocumentNo
              ? <>{row.qrDocumentNo}<small>NIF {row.qrIssuerTaxId}{row.qrDocumentDate ? ` · ${formatMovementDate(row.qrDocumentDate)}` : ''}{row.qrGrossTotal !== null ? ` · ${euro.format(row.qrGrossTotal)}` : ''}</small></>
              : <small>sem QR lido</small>}</td>
            <td>{row.entryReference
              ? <span className="inline-check"><Link to={`/movimentos/${row.entryReference}`}>{row.entryReference}</Link>{row.canDelete && <button className="text-link" type="button" disabled={busy} onClick={() => save(row, { entryReference: null })}>desligar</button>}</span>
              : <form className="inline-check" onSubmit={(event) => { event.preventDefault(); save(row, { entryReference: (references[row.id] ?? '').trim().toUpperCase() || null }) }}>
                <input aria-label={`Lançamento de ${row.fileName}`} value={references[row.id] ?? ''} onChange={(event) => setReferences((current) => ({ ...current, [row.id]: event.target.value }))} placeholder="referência" />
                <button className="button button-secondary" type="submit" disabled={busy || !(references[row.id] ?? '').trim()}>Ligar</button>
              </form>}</td>
            <td><button className="icon-button" type="button" aria-label={`Apagar ${row.fileName}`} title={row.canDelete ? 'Apagar' : 'Documento de um lançamento conferido: conserva-se'} disabled={busy || !row.canDelete} onClick={() => remove(row)}><Trash2 size={16} /></button></td>
          </tr>)}</tbody>
        </table></div>}
    </section>

    <p className="statement-note">O código QR lê-se pelas especificações técnicas da AT (Portaria n.º 195/2020). A impressão digital SHA-256 permite provar que o ficheiro não mudou desde que foi arquivado. Um documento de um lançamento conferido conserva-se: para o mudar, reabra o lançamento.</p>
  </div>
}
