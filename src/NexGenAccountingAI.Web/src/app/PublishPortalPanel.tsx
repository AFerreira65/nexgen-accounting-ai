import { Check, CircleAlert, Globe, Send, X } from 'lucide-react'
import { useState } from 'react'
import { createPortal } from 'react-dom'
import { Link } from 'react-router-dom'
import { PORTAL_CATEGORIES } from '../pages/ClientPortalApp'
import type { EmailAttachmentSource } from './SendEmailPanel'
import { problemMessage, useWorkContext } from './WorkContext'

/**
 * O painel «Publicar no portal do cliente»: guarda uma fotografia do que o ecrã mostra (listagem, balancete…) ou do
 * relato, em PDF ou Excel, e os clientes convidados passam a vê-la no portal.
 */
export function PublishPortalPanel({ source, onClose }: { source: EmailAttachmentSource; onClose: () => void }) {
  const { activeCompany, user } = useWorkContext()
  const [title, setTitle] = useState(source.title)
  const [category, setCategory] = useState<typeof PORTAL_CATEGORIES[number]['key']>(/balancete/i.test(source.title) ? 'TrialBalance' : source.kind === 'pack' ? 'Reports' : 'Management')
  const [format, setFormat] = useState<'pdf' | 'xlsx'>('pdf')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [done, setDone] = useState(false)

  async function publish() {
    setBusy(true); setError('')
    try {
      const attachment = source.kind === 'table' ? { format, table: { title: source.title, rows: source.rows } } : { format: 'pdf', pack: source.request }
      const response = await fetch(`/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/portal/items/export`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ category, title, attachment }),
      })
      if (!response.ok) throw new Error(await problemMessage(response))
      setDone(true)
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível publicar.')
    } finally {
      setBusy(false)
    }
  }

  return createPortal(<div className="email-overlay no-print" role="dialog" aria-modal="true" aria-label="Publicar no portal do cliente"><div className="email-dialog"><section className="panel settings-panel">
    <div className="panel-heading"><div><h2><Globe size={17} /> Publicar no portal do cliente</h2><p>Os clientes de {activeCompany.legalName.replace(' (Demonstração)', '')} passam a ver este ficheiro. É uma fotografia de agora: se os dados mudarem, publique de novo. Gestão dos clientes em <Link to="/portal-cliente">Portal do cliente</Link>.</p></div>
      <button className="icon-button" type="button" aria-label="Fechar" onClick={onClose}><X size={16} /></button></div>
    {error && <p className="save-status error" role="alert"><CircleAlert size={15} /> {error}</p>}
    {done && !error && <p className="save-status success"><Check size={15} /> Publicado no portal.</p>}
    <div className="settings-grid">
      <label><span>Título que o cliente vê</span><input value={title} onChange={(event) => setTitle(event.target.value)} /></label>
      <label><span>Tipo</span><select value={category} onChange={(event) => setCategory(event.target.value as typeof category)}>{PORTAL_CATEGORIES.map((item) => <option key={item.key} value={item.key}>{item.label}</option>)}</select></label>
      {source.kind === 'table' && <label><span>Formato</span><select value={format} onChange={(event) => setFormat(event.target.value as 'pdf' | 'xlsx')}><option value="pdf">PDF</option><option value="xlsx">Excel</option></select></label>}
    </div>
    <footer className="settings-actions"><button className="button button-primary" type="button" disabled={busy || done || !title.trim()} onClick={() => void publish()}><Send size={16} /> {busy ? 'A publicar…' : 'Publicar'}</button></footer>
  </section></div></div>, document.body)
}
