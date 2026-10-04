import { BookText, Database, Info } from 'lucide-react'
import { useEffect, useState } from 'react'
import { euro, type FiscalSource, type FiscalTable } from '../app/demo-data'
import { usePublishRecord } from '../app/RecordContext'
import { vatRegionLabel } from '../app/vatClassification'
import { useWorkContext } from '../app/WorkContext'

const percent = new Intl.NumberFormat('pt-PT', { style: 'percent', minimumFractionDigits: 0, maximumFractionDigits: 2 })

function SourceNote({ source }: { source: FiscalSource }) {
  return <p className="fiscal-source">
    <span className={`badge ${source.status === 'Oficial' ? 'success' : 'warning'}`}>{source.status === 'Oficial' ? 'Texto oficial' : 'Por confirmar'}</span>
    {source.reference}
  </p>
}

export function FiscalTablesPage() {
  const { activeCompany, source, fiscalTable } = useWorkContext()
  const onDatabase = source === 'database'
  const [year, setYear] = useState(activeCompany.settings.defaultFiscalYear ?? new Date().getUTCFullYear())
  const [table, setTable] = useState<FiscalTable | null>(null)
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState('')
  usePublishRecord(undefined, undefined)

  useEffect(() => {
    if (!onDatabase) return
    let cancelled = false
    setLoaded(false)
    setError('')
    void fiscalTable(year)
      .then((value) => { if (!cancelled) { setTable(value); setLoaded(true) } })
      .catch((problem: unknown) => { if (!cancelled) { setTable(null); setLoaded(true); setError(problem instanceof Error ? problem.message : 'Não foi possível carregar a tabela fiscal.') } })
    return () => { cancelled = true }
  }, [fiscalTable, onDatabase, year])

  if (!onDatabase) {
    return <div className="page-stack">
      <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Tabelas fiscais</h1><p>As taxas que mudam de ano para ano, com a fonte de cada uma.</p></div><span className="connection-badge demonstration"><Database size={15} /> Modo de demonstração</span></header>
      <section className="panel empty-state"><span className="empty-icon"><BookText /></span><h2>Requer ligação à base de dados</h2><p>As tabelas fiscais são servidas pela API.</p></section>
    </div>
  }

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Fiscal</p><h1>Tabelas fiscais</h1><p>As taxas que mudam de ano para ano e que a aplicação propõe — na estimativa de IRC e nos códigos de IVA por omissão. Cada grupo indica a fonte e se foi lido no texto oficial.</p></div><span className="connection-badge database"><Database size={15} /> Ligado à base de dados</span></header>

    <div className="filterbar">
      <label className="inline-check">Ano <input type="number" aria-label="Ano" min="2000" max="2100" value={year} onChange={(event) => setYear(Number(event.target.value) || year)} /></label>
    </div>

    {error && <p className="save-status error">{error}</p>}
    {loaded && !table && !error && <section className="panel empty-state"><span className="empty-icon"><BookText /></span><h2>Sem tabela para {year}</h2><p>O catálogo não cobre este ano. Indique as taxas diretamente onde forem precisas.</p></section>}

    {table && <>
      <p className="mr-hint"><Info size={13} /> {table.disclaimer}{table.carriedForward && ` O catálogo ainda não tem ${table.year}: os valores são os do último ano conhecido.`}</p>

      <section className="panel">
        <div className="panel-heading"><div><h2>IRC</h2><p>Taxa geral e taxa reduzida das PME e Small Mid Cap.</p></div></div>
        <div className="table-wrap"><table><tbody>
          <tr><td>Taxa geral</td><td className="numeric strong">{percent.format(table.irc.generalRate)}</td></tr>
          <tr><td>PME — até {euro.format(table.irc.smeThreshold)} de matéria coletável</td><td className="numeric strong">{percent.format(table.irc.smeRate)}</td></tr>
        </tbody></table></div>
        <SourceNote source={table.irc.source} />
      </section>

      <section className="panel">
        <div className="panel-heading"><div><h2>Derrama estadual</h2><p>Por parcelas do lucro tributável. A derrama municipal varia por município e não está nesta tabela.</p></div></div>
        <div className="table-wrap"><table>
          <thead><tr><th>Lucro tributável</th><th className="numeric">Taxa</th></tr></thead>
          <tbody>{table.stateSurtax.map((bracket) => <tr key={bracket.from}>
            <td>{bracket.to === null ? `Superior a ${euro.format(bracket.from)}` : `De mais de ${euro.format(bracket.from)} até ${euro.format(bracket.to)}`}</td>
            <td className="numeric strong">{percent.format(bracket.rate)}</td>
          </tr>)}</tbody>
        </table></div>
        <SourceNote source={table.stateSurtaxSource} />
      </section>

      <section className="panel">
        <div className="panel-heading"><div><h2>IVA</h2><p>Taxas por região.</p></div></div>
        <div className="table-wrap"><table>
          <thead><tr><th>Região</th><th className="numeric">Reduzida</th><th className="numeric">Intermédia</th><th className="numeric">Normal</th><th>Fonte</th></tr></thead>
          <tbody>{table.vat.map((rates) => <tr key={rates.region}>
            <td>{vatRegionLabel(rates.region)}</td>
            <td className="numeric">{percent.format(rates.reduced)}</td>
            <td className="numeric">{percent.format(rates.intermediate)}</td>
            <td className="numeric strong">{percent.format(rates.normal)}</td>
            <td><span className={`badge ${rates.source.status === 'Oficial' ? 'success' : 'warning'}`} title={rates.source.reference}>{rates.source.status === 'Oficial' ? 'Texto oficial' : 'Por confirmar'}</span></td>
          </tr>)}</tbody>
        </table></div>
      </section>

      <section className="panel">
        <div className="panel-heading"><div><h2>Tributações autónomas</h2><p>As taxas mais comuns do artigo 88.º do CIRC.</p></div></div>
        {table.autonomousTaxation.length > 0 && <div className="table-wrap"><table>
          <thead><tr><th>Encargo</th><th className="numeric">Taxa</th></tr></thead>
          <tbody>{table.autonomousTaxation.map((item) => <tr key={item.key}><td>{item.description}</td><td className="numeric strong">{percent.format(item.rate)}</td></tr>)}</tbody>
        </table></div>}
        <SourceNote source={table.autonomousTaxationSource} />
      </section>
    </>}
  </div>
}
