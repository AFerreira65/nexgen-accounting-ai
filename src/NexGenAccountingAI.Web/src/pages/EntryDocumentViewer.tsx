import { CircleAlert, ExternalLink, FileUp, X } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { euro, type DocumentAttachmentRow } from '../app/demo-data'
import { readInvoiceQr } from '../app/qrScan'
import { useWorkContext } from '../app/WorkContext'

// Os browsers mostram PDF, imagens comuns e XML; TIFF não.
const canPreview = (row: DocumentAttachmentRow) => row.contentType !== 'image/tiff'
const isImage = (row: DocumentAttachmentRow) => row.contentType.startsWith('image/')

/**
 * O documento ao lado das linhas do editor de lançamentos. Escolhe-se entre os
 * documentos já ligados ao lançamento e os do arquivo ainda sem lançamento, ou
 * arquiva-se um novo; a ligação faz-se ao guardar o lançamento.
 */
export function EntryDocumentViewer({ reference, chosen, onChoose, onClose }: {
  reference: string | null
  chosen: DocumentAttachmentRow | null
  onChoose: (row: DocumentAttachmentRow | null) => void
  onClose: () => void
}) {
  const { activeCompany, attachments, uploadAttachment, attachmentUrl } = useWorkContext()
  const companyId = activeCompany.companyId
  const inputRef = useRef<HTMLInputElement>(null)
  const [linked, setLinked] = useState<DocumentAttachmentRow[]>([])
  const [unlinked, setUnlinked] = useState<DocumentAttachmentRow[]>([])
  const [search, setSearch] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    let cancelled = false
    const timer = window.setTimeout(() => {
      void Promise.all([
        reference ? attachments(companyId, { entryReference: reference }) : Promise.resolve([]),
        attachments(companyId, { unlinked: true, search: search.trim() || undefined }),
      ]).then(([own, free]) => {
        if (cancelled) return
        setLinked(own)
        setUnlinked(free)
        // Ao corrigir um lançamento com documentos, mostra logo o primeiro.
        if (!chosen && own.length > 0) onChoose(own[0])
      }).catch(() => { if (!cancelled) setError('Não foi possível ler o arquivo digital.') })
    }, search ? 250 : 0)
    return () => { cancelled = true; window.clearTimeout(timer) }
    // `chosen` e `onChoose` ficam de fora: a escolha automática é só na abertura.
  }, [attachments, companyId, reference, search])

  const upload = async (file: File | undefined) => {
    if (inputRef.current) inputRef.current.value = ''
    if (!file) return
    setBusy(true)
    setError('')
    try {
      // Fica no arquivo sem lançamento até este ser guardado.
      const row = await uploadAttachment(companyId, file, { qr: await readInvoiceQr(file) })
      setUnlinked((current) => [row, ...current])
      onChoose(row)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível arquivar o documento.')
    } finally {
      setBusy(false)
    }
  }

  const options = [...linked, ...unlinked.filter((row) => !linked.some((own) => own.id === row.id))]
  if (chosen && !options.some((row) => row.id === chosen.id)) options.unshift(chosen)
  const label = (row: DocumentAttachmentRow) => `${row.fileName}${row.qrDocumentNo ? ` · ${row.qrDocumentNo}` : ''}${row.entryReference ? '' : ' · sem lançamento'}`
  const url = chosen ? attachmentUrl(companyId, chosen.id) : ''

  return <aside className="entry-document" aria-label="Documento do lançamento">
    <div className="entry-document-toolbar">
      <select aria-label="Documento a ver" value={chosen?.id ?? ''} onChange={(event) => onChoose(options.find((row) => row.id === event.target.value) ?? null)}>
        <option value="">{options.length === 0 ? 'Sem documentos no arquivo' : 'Escolher documento…'}</option>
        {options.map((row) => <option key={row.id} value={row.id}>{label(row)}</option>)}
      </select>
      <input aria-label="Procurar no arquivo" placeholder="Procurar" value={search} onChange={(event) => setSearch(event.target.value)} />
      <input ref={inputRef} type="file" accept=".pdf,.png,.jpg,.jpeg,.webp,.tif,.tiff,.xml" className="sr-only" aria-label="Arquivar um documento para este lançamento" onChange={(event) => void upload(event.target.files?.[0])} />
      <button className="icon-button" type="button" title="Arquivar um documento" aria-label="Arquivar um documento" disabled={busy} onClick={() => inputRef.current?.click()}><FileUp size={16} /></button>
      {chosen && <a className="icon-button" href={url} target="_blank" rel="noreferrer" title="Abrir noutra janela" aria-label="Abrir noutra janela"><ExternalLink size={16} /></a>}
      <button className="icon-button" type="button" aria-label="Fechar o documento" onClick={onClose}><X size={16} /></button>
    </div>
    {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
    {chosen?.qrDocumentNo && <p className="entry-document-qr">
      Código QR: <strong>{chosen.qrDocumentNo}</strong>{chosen.qrDocumentDate ? ` de ${chosen.qrDocumentDate}` : ''}{chosen.qrIssuerTaxId ? ` · emitente ${chosen.qrIssuerTaxId}` : ''}
      {chosen.qrGrossTotal !== null && <> · total <strong>{euro.format(chosen.qrGrossTotal)}</strong></>}
      {chosen.qrTaxTotal !== null && <> · IVA {euro.format(chosen.qrTaxTotal)}</>}
    </p>}
    <div className="entry-document-frame">
      {!chosen && <p className="statement-note">Escolha um documento do arquivo, ou arquive um novo, para o ver enquanto lança.</p>}
      {chosen && !canPreview(chosen) && <p className="statement-note">O navegador não mostra ficheiros TIFF: <a href={url} target="_blank" rel="noreferrer">abra-o noutra janela</a>.</p>}
      {chosen && canPreview(chosen) && (isImage(chosen)
        ? <img src={url} alt={chosen.fileName} />
        : <iframe key={chosen.id} src={url} title={chosen.fileName} />)}
    </div>
  </aside>
}
