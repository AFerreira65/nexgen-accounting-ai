import { CircleAlert, FileUp, Paperclip, Trash2 } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { euro, type DocumentAttachmentRow } from '../app/demo-data'
import { readInvoiceQr } from '../app/qrScan'
import { useWorkContext } from '../app/WorkContext'

/** Os documentos do arquivo digital ligados a um lançamento, com o envio de novos. */
export function MovementDocuments({ reference }: { reference: string }) {
  const { activeCompany, attachments, uploadAttachment, deleteAttachment, attachmentUrl } = useWorkContext()
  const companyId = activeCompany.companyId
  const inputRef = useRef<HTMLInputElement>(null)
  const [rows, setRows] = useState<DocumentAttachmentRow[] | null>(null)
  const [reload, setReload] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    void attachments(companyId, { entryReference: reference })
      .then((value) => { if (!controller.signal.aborted) setRows(value) })
      .catch(() => { if (!controller.signal.aborted) setRows([]) })
    return () => controller.abort()
  }, [attachments, companyId, reference, reload])

  async function run(action: () => Promise<void>) {
    setBusy(true)
    setError('')
    try {
      await action()
      setReload((value) => value + 1)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível concluir.')
    } finally {
      setBusy(false)
    }
  }

  const upload = (files: FileList | null) => {
    const list = [...(files ?? [])]
    if (inputRef.current) inputRef.current.value = ''
    if (list.length === 0) return
    void run(async () => {
      for (const file of list) {
        await uploadAttachment(companyId, file, { entryReference: reference, qr: await readInvoiceQr(file) })
      }
    })
  }

  return <section className="panel" id="documentos">
    <div className="panel-heading"><div><h2>Documentos</h2><p>Os documentos do <Link to="/arquivo">arquivo digital</Link> que suportam este lançamento.</p></div>
      <input ref={inputRef} type="file" multiple accept=".pdf,.png,.jpg,.jpeg,.webp,.tif,.tiff,.xml" className="sr-only" aria-label="Anexar documentos ao lançamento" onChange={(event) => upload(event.target.files)} />
      <button className="button button-secondary" type="button" disabled={busy} onClick={() => inputRef.current?.click()}><FileUp size={16} /> Anexar</button>
    </div>
    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
    {rows && rows.length === 0 && <p className="statement-note">Sem documentos anexados.</p>}
    {rows && rows.length > 0 && <ul className="attachment-list">{rows.map((row) => <li key={row.id}>
      <Paperclip size={15} />
      <a href={attachmentUrl(companyId, row.id)} target="_blank" rel="noreferrer">{row.fileName}</a>
      {row.qrDocumentNo && <small>{row.qrDocumentNo}{row.qrGrossTotal !== null ? ` · ${euro.format(row.qrGrossTotal)}` : ''}</small>}
      {row.canDelete && <button className="icon-button" type="button" aria-label={`Apagar ${row.fileName}`} disabled={busy} onClick={() => void run(() => deleteAttachment(companyId, row.id))}><Trash2 size={15} /></button>}
    </li>)}</ul>}
  </section>
}
