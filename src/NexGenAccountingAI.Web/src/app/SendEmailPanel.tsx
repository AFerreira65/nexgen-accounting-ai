import { Check, CircleAlert, Mail, Send, X } from 'lucide-react'
import { useState } from 'react'
import { createPortal } from 'react-dom'
import { Link } from 'react-router-dom'
import { problemMessage, useWorkContext } from './WorkContext'

/** O que se envia em anexo: o que o ecrã mostra (linhas) ou um relato. O servidor gera o PDF ou o Excel. */
export type EmailAttachmentSource =
  | { kind: 'table'; title: string; rows: (string | number)[][] }
  | { kind: 'pack'; request: unknown; title: string }

/**
 * O painel «Enviar por email» (F4E-04): para quem, o assunto e o texto, e o formato do anexo. Envia com a conta
 * de email do utilizador (Definições → A minha conta de email); sem conta, o servidor explica o que falta.
 */
export function SendEmailPanel({ source, onClose }: { source: EmailAttachmentSource; onClose: () => void }) {
  const { activeCompany, user } = useWorkContext()
  const [to, setTo] = useState('')
  const [subject, setSubject] = useState(`${source.title} — ${activeCompany.legalName.replace(' (Demonstração)', '')}`)
  const [body, setBody] = useState('Bom dia,\n\nSegue em anexo o documento pedido.\n\nCumprimentos.')
  const [format, setFormat] = useState<'pdf' | 'xlsx'>('pdf')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [sent, setSent] = useState('')

  async function send() {
    const recipients = to.split(/[;,\s]+/).map((item) => item.trim()).filter(Boolean)
    if (recipients.length === 0) { setError('Indique pelo menos um destinatário.'); return }
    setBusy(true); setError(''); setSent('')
    try {
      const attachment = source.kind === 'table'
        ? { format, table: { title: source.title, rows: source.rows } }
        : { format: 'pdf', pack: source.request }
      const response = await fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/email/send`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ to: recipients, subject, body, attachments: [attachment] }),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      setSent(`Enviado para ${recipients.join(', ')}.`)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível enviar o email.')
    } finally {
      setBusy(false)
    }
  }

  return createPortal(<div className="email-overlay no-print" role="dialog" aria-modal="true" aria-label="Enviar por email"><div className="email-dialog"><section className="panel settings-panel">
    <div className="panel-heading"><div><h2><Mail size={17} /> Enviar por email</h2><p>Envia o anexo com a sua conta de email. Ainda não a configurou? <Link to="/definicoes">Definições → A minha conta de email</Link>.</p></div>
      <button className="icon-button" type="button" aria-label="Fechar" onClick={onClose}><X size={16} /></button></div>
    {error && <p className="save-status error" role="alert"><CircleAlert size={15} /> {error}</p>}
    {sent && !error && <p className="save-status success"><Check size={15} /> {sent}</p>}
    <div className="settings-grid">
      <label><span>Para (separe vários por vírgula)</span><input value={to} onChange={(event) => setTo(event.target.value)} placeholder="cliente@empresa.pt" /></label>
      <label><span>Assunto</span><input value={subject} onChange={(event) => setSubject(event.target.value)} /></label>
      {source.kind === 'table' && <label><span>Formato do anexo</span><select value={format} onChange={(event) => setFormat(event.target.value as 'pdf' | 'xlsx')}><option value="pdf">PDF</option><option value="xlsx">Excel</option></select></label>}
      <label style={{ gridColumn: '1 / -1' }}><span>Mensagem</span><textarea rows={5} value={body} onChange={(event) => setBody(event.target.value)} /></label>
    </div>
    <footer className="settings-actions"><button className="button button-primary" type="button" disabled={busy} onClick={() => void send()}><Send size={16} /> {busy ? 'A enviar…' : 'Enviar'}</button></footer>
  </section></div></div>, document.body)
}
