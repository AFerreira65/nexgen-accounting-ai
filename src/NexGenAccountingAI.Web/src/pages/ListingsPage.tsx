import { Database, LayoutTemplate, ListFilter, Plus, Search, Star } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { signalLoadFailure } from '../app/loadFailure'
import { SCOPE_LABELS, type ListingTemplate, type SavedListing } from '../app/listingLabels'
import { useWorkContext } from '../app/WorkContext'

/**
 * As listagens à medida (F4D-01): as que o utilizador guardou para si, para a empresa ou para
 * o gabinete. Cada uma é uma pergunta sobre a contabilidade, sem escrever uma linha de SQL.
 */
export function ListingsPage() {
  const { activeCompany, source, user } = useWorkContext()
  const navigate = useNavigate()
  const [listings, setListings] = useState<SavedListing[] | null>(null)
  const [templates, setTemplates] = useState<ListingTemplate[]>([])
  const [search, setSearch] = useState('')
  const [error, setError] = useState('')
  const root = `/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/listings`

  useEffect(() => {
    if (source !== 'database') return
    let cancelled = false
    void fetch(root)
      .then(async (response) => response.ok ? await response.json() as SavedListing[] : Promise.reject(new Error()))
      .then((list) => { if (!cancelled) setListings(list) })
      .catch(() => { signalLoadFailure(); setError('Não foi possível ler as listagens.') })
    void fetch(`${root}/templates`)
      .then(async (response) => response.ok ? await response.json() as ListingTemplate[] : [])
      .then((list) => { if (!cancelled) setTemplates(list) })
      .catch(() => { /* os modelos são um complemento */ })
    return () => { cancelled = true }
  }, [root, source])

  const shown = useMemo(() => {
    const term = search.trim().toLowerCase()
    return (listings ?? []).filter((item) => term === '' || item.name.toLowerCase().includes(term) || (item.description ?? '').toLowerCase().includes(term))
  }, [listings, search])

  if (source !== 'database') {
    return <div className="page-stack"><header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Listagens à medida</h1><p>As listagens precisam da base de dados; em demonstração não estão disponíveis.</p></div></header></div>
  }

  return <div className="page-stack">
    <header className="page-heading detail-heading">
      <div><p className="eyebrow">Mapas</p><h1>Listagens à medida</h1><p>Perguntas à contabilidade desenhadas por si: escolha as colunas, filtre, agrupe em vários níveis e guarde. Cada linha leva ao lançamento ou ao terceiro de onde veio.</p></div>
      <span className="connection-badge database"><Database size={15} />Dados da BD</span>
    </header>
    {error && <p className="save-status error" role="alert">{error}</p>}
    <section className="panel">
      <div className="panel-heading">
        <h2><ListFilter size={17} /> As suas listagens</h2>
        <button type="button" className="button button-primary" onClick={() => navigate('/listagens/nova')}><Plus size={15} /> Nova listagem</button>
      </div>
      <div className="filterbar">
        <label className="inline-check"><Search size={14} /> <input aria-label="Procurar listagens" placeholder="Procurar por nome" value={search} onChange={(event) => setSearch(event.target.value)} /></label>
      </div>
      {listings && listings.length === 0 && <p className="empty-state">Ainda não há listagens guardadas. Comece uma nova e guarde-a para a reutilizar.</p>}
      {shown.length > 0 && <div className="table-wrap"><table>
        <thead><tr><th>Nome</th><th>Descrição</th><th>Quem a vê</th></tr></thead>
        <tbody>{shown.map((item) => <tr key={item.id}>
          <td><Link className="primary-cell" to={`/listagens/${item.id}`}>{item.isFavorite && <Star size={13} aria-label="Favorita" />} {item.name}</Link></td>
          <td>{item.description ?? ''}</td>
          <td>{SCOPE_LABELS[item.scope]}{item.mine ? '' : ' (de outro utilizador)'}</td>
        </tr>)}</tbody>
      </table></div>}
    </section>
    <section className="panel">
      <div className="panel-heading"><h2><LayoutTemplate size={17} /> Modelos para começar</h2></div>
      <div className="maps-templates">
        {templates.map((template) => <article key={template.key} className="maps-template">
          <h3><Link to={`/listagens/modelo/${template.key}`}>{template.name}</Link></h3>
          <p>{template.description}</p>
        </article>)}
      </div>
    </section>
  </div>
}
