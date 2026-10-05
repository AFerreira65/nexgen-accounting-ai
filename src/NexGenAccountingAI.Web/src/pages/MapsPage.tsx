import { Database, FileSpreadsheet, LayoutTemplate, Plus, Trash2 } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { signalLoadFailure } from '../app/loadFailure'
import { useWorkContext } from '../app/WorkContext'

export type SavedMap = { id: string; name: string; description: string | null; createdAtUtc: string | null }
export type MapTemplateSummary = { key: string; name: string; description: string; excludeClosing: boolean }
type Listing = { maps: SavedMap[]; templates: MapTemplateSummary[] }

/**
 * Os mapas à medida (F4B-03): os que a empresa guardou e os modelos do sistema de que
 * se parte. Um modelo abre-se tal como está; guardá-lo faz dele um mapa da empresa.
 */
export function MapsPage() {
  const { activeCompany, source, user } = useWorkContext()
  const navigate = useNavigate()
  const [listing, setListing] = useState<Listing | null>(null)
  const [error, setError] = useState('')
  const root = `/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/maps`

  const load = useCallback(() => {
    if (source !== 'database') return
    void fetch(root)
      .then(async (response) => response.ok ? await response.json() as Listing : Promise.reject(new Error()))
      .then(setListing)
      .catch(() => { signalLoadFailure(); setError('Não foi possível ler os mapas.') })
  }, [root, source])
  useEffect(load, [load])

  async function remove(map: SavedMap) {
    if (!window.confirm(`Apagar o mapa «${map.name}»?`)) return
    const response = await fetch(`${root}/${map.id}`, { method: 'DELETE' })
    if (!response.ok) { setError('Não foi possível apagar o mapa.'); return }
    load()
  }

  if (source !== 'database') {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Mapas</p><h1>Mapas à medida</h1><p>Os mapas desenhados pelo utilizador precisam da base de dados; em demonstração não estão disponíveis.</p></div></header>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading detail-heading">
      <div><p className="eyebrow">Mapas</p><h1>Mapas à medida</h1><p>Mapas desenhados por si — linhas de contas e fórmulas, nas colunas que escolher — calculados sobre a contabilidade de cada vez que os abre.</p></div>
      <span className="connection-badge database"><Database size={15} />Dados da BD</span>
    </header>
    {error && <p className="save-status error" role="alert">{error}</p>}
    <section className="panel">
      <div className="panel-heading"><h2><FileSpreadsheet size={17} /> Mapas da empresa</h2><button type="button" className="button button-primary" onClick={() => navigate('/mapas/novo')}><Plus size={15} /> Novo mapa em branco</button></div>
      {listing && listing.maps.length === 0 && <p className="empty-state">Ainda não há mapas guardados. Abra um modelo abaixo e guarde-o, ou comece um em branco.</p>}
      {listing && listing.maps.length > 0 && <div className="table-wrap"><table>
        <thead><tr><th>Nome</th><th>Descrição</th><th>Criado</th><th className="sr-only">Ações</th></tr></thead>
        <tbody>{listing.maps.map((map) => <tr key={map.id}>
          <td><Link className="primary-cell" to={`/mapas/${map.id}`}>{map.name}</Link></td>
          <td>{map.description ?? ''}</td>
          <td>{map.createdAtUtc ? new Date(map.createdAtUtc).toLocaleDateString('pt-PT') : ''}</td>
          <td className="numeric"><button type="button" className="icon-button" aria-label={`Apagar ${map.name}`} onClick={() => void remove(map)}><Trash2 size={15} /></button></td>
        </tr>)}</tbody>
      </table></div>}
    </section>
    <section className="panel">
      <div className="panel-heading"><h2><LayoutTemplate size={17} /> Modelos para começar</h2></div>
      <div className="maps-templates">
        {(listing?.templates ?? []).map((template) => <article key={template.key} className="maps-template">
          <h3><Link to={`/mapas/modelo/${template.key}`}>{template.name}</Link></h3>
          <p>{template.description}</p>
        </article>)}
      </div>
    </section>
  </div>
}
