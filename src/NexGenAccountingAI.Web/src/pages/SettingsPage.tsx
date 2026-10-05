import { Check, Database, Save, SlidersHorizontal } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { type CompanySettings, useWorkContext } from '../app/WorkContext'

export function SettingsPage() {
  const { activeCompany, source, saveSettings } = useWorkContext()
  const [form, setForm] = useState<CompanySettings>(activeCompany.settings)
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle')

  useEffect(() => {
    setForm(activeCompany.settings)
    setStatus('idle')
  }, [activeCompany])

  async function submit(event: FormEvent) {
    event.preventDefault()
    setStatus('saving')
    try {
      await saveSettings(form)
      setStatus('saved')
    } catch {
      setStatus('error')
    }
  }

  return (
    <div className="page-stack">
      <header className="page-heading">
        <div><p className="eyebrow">Empresa e utilizador</p><h1>Preferências de trabalho</h1><p>Configuração exclusiva de {activeCompany.legalName} para o utilizador atual.</p></div>
        <span className={`connection-badge ${source}`}><Database size={15} />{source === 'database' ? 'Ligado à base de dados' : 'Modo de demonstração'}</span>
      </header>

      <form className="settings-layout" onSubmit={submit}>
        <section className="panel settings-panel">
          <div className="panel-heading"><div><h2>Experiência e período</h2><p>Estas opções não afetam as restantes empresas.</p></div><SlidersHorizontal size={19} /></div>
          <div className="settings-grid">
            <label><span>Idioma e região</span><select value={form.culture} onChange={(event) => setForm({ ...form, culture: event.target.value })}><option value="pt-PT">Português (Portugal)</option>{form.culture !== 'pt-PT' && <option value={form.culture}>{form.culture} (sem tradução: a aplicação está em português)</option>}</select></label>
            <label><span>Fuso horário</span><select value={form.timeZone} onChange={(event) => setForm({ ...form, timeZone: event.target.value })}><option value="Europe/Lisbon">Lisboa / Madeira</option><option value="Atlantic/Azores">Açores</option></select></label>
            <label><span>Exercício predefinido</span><input type="number" min="2000" max="2200" value={form.defaultFiscalYear ?? ''} onChange={(event) => setForm({ ...form, defaultFiscalYear: event.target.value ? Number(event.target.value) : null })} /></label>
            <label><span>Tema</span><select value={form.theme} onChange={(event) => setForm({ ...form, theme: event.target.value as CompanySettings['theme'] })}><option value="System">Usar sistema</option><option value="Light">Claro</option><option value="Dark">Escuro</option></select></label>
            <label><span>Tamanho do texto</span><select value={form.textSize} onChange={(event) => setForm({ ...form, textSize: event.target.value as CompanySettings['textSize'] })}><option value="Normal">Normal</option><option value="Large">Grande</option><option value="Larger">Maior</option></select></label>
            <label><span>Linhas por página</span><select value={form.pageSize} onChange={(event) => setForm({ ...form, pageSize: Number(event.target.value) })}><option value="25">25 linhas</option><option value="50">50 linhas</option><option value="100">100 linhas</option><option value="200">200 linhas</option></select></label>
            <label className="toggle-field"><input type="checkbox" checked={form.compactMode} onChange={(event) => setForm({ ...form, compactMode: event.target.checked })} /><span><strong>Modo compacto</strong><small>Mostra mais informação nas grelhas e painéis.</small></span></label>
          </div>
          <footer className="settings-actions">
            {status === 'saved' && <span className="save-status success"><Check size={15} />Preferências guardadas</span>}
            {status === 'error' && <span className="save-status error">Não foi possível guardar.</span>}
            <button className="button button-primary" type="submit" disabled={status === 'saving'}><Save size={16} />{status === 'saving' ? 'A guardar…' : 'Guardar preferências'}</button>
          </footer>
        </section>
      </form>
    </div>
  )
}
