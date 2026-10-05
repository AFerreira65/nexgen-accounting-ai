import { CircleAlert, Database, Printer } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { csvFilename, downloadCsv, toCsv } from '../app/csv'
import { ExportButtons } from '../app/ExportButtons'
import type { DataDictionaryTable, DataDictionaryTableSummary } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

function keyLabel(column: DataDictionaryTable['columns'][number]): string {
  const parts: string[] = []
  if (column.primaryKey) parts.push('PK')
  if (column.foreignKey) parts.push('FK')
  if (column.unique && !column.primaryKey) parts.push('Único')
  else if (column.indexed && !column.foreignKey) parts.push('Índice')
  return parts.join(' · ') || '—'
}

export function DataDictionaryPage() {
  const { dataDictionary, dataDictionaryTable } = useWorkContext()
  const [params, setParams] = useSearchParams()
  const [tables, setTables] = useState<DataDictionaryTableSummary[] | null>(null)
  const [detail, setDetail] = useState<DataDictionaryTable | null>(null)
  const [error, setError] = useState('')

  const selected = params.get('tabela') ?? ''

  useEffect(() => {
    void dataDictionary()
      .then((list) => { setTables(list); setError('') })
      .catch((problem: unknown) => setError(problem instanceof Error ? problem.message : 'Não foi possível carregar o dicionário.'))
  }, [dataDictionary])

  useEffect(() => {
    if (!selected && tables && tables.length > 0) {
      setParams({ tabela: `${tables[0].schema}/${tables[0].name}` }, { replace: true })
    }
  }, [selected, tables, setParams])

  useEffect(() => {
    if (!selected.includes('/')) { setDetail(null); return }
    const [schema, name] = selected.split('/')
    void dataDictionaryTable(schema, name)
      .then((body) => { setDetail(body); setError('') })
      .catch((problem: unknown) => { setDetail(null); setError(problem instanceof Error ? problem.message : 'Não foi possível carregar a tabela.') })
  }, [selected, dataDictionaryTable])

  const grouped = useMemo(() => {
    const bySchema = new Map<string, DataDictionaryTableSummary[]>()
    for (const table of tables ?? []) {
      const list = bySchema.get(table.schema) ?? []
      list.push(table)
      bySchema.set(table.schema, list)
    }
    return [...bySchema.entries()].sort(([a], [b]) => a.localeCompare(b))
  }, [tables])

  function exportCsv() {
    if (!detail) return
    const rows: (string | number)[][] = [
      [`${detail.schema}.${detail.name}`, detail.purpose],
      [],
      ['Coluna', 'Tipo .NET', 'Tipo SQL', 'Nulo', 'Chave', 'Referência', 'Descrição'],
      ...detail.columns.map((column) => [
        column.name, column.clrType, column.sqlType, column.nullable ? 'Sim' : 'Não',
        keyLabel(column), column.references ?? '', column.description,
      ]),
    ]
    downloadCsv(csvFilename('dicionario', `${detail.schema}-${detail.name}`), toCsv(rows))
  }

  const printedOn = new Intl.DateTimeFormat('pt-PT', { dateStyle: 'long', timeStyle: 'short' }).format(new Date())

  return <div className="page-stack data-dictionary">
    <header className="page-heading no-print"><div><p className="eyebrow">Opções</p><h1>Dicionário de dados</h1><p>A estrutura das tabelas da base de dados, com o significado de cada coluna. Semelhante ao Dicionário de Dados do PHC CS.</p></div><span className="connection-badge database"><Database size={15} /> Estrutura do modelo</span></header>

    {detail && <div className="print-only dictionary-print-head">
      <h1>Dicionário de dados — {detail.schema}.{detail.name}</h1>
      <p>{detail.purpose || 'Sem descrição.'}</p>
      <p className="print-meta">{detail.columnCount} colunas · Impresso em {printedOn}</p>
    </div>}

    <section className="panel no-print">
      <div className="filterbar">
        <label>
          <span className="sr-only">Tabela</span>
          <select value={selected} onChange={(event) => setParams({ tabela: event.target.value })}>
            {!selected && <option value="">Escolher tabela…</option>}
            {grouped.map(([schema, list]) => <optgroup key={schema} label={schema}>
              {list.map((table) => <option key={`${schema}/${table.name}`} value={`${schema}/${table.name}`}>{table.name} ({table.columnCount})</option>)}
            </optgroup>)}
          </select>
        </label>
        {detail && <button className="button button-secondary" type="button" onClick={() => window.print()}><Printer size={15} /> Imprimir / PDF</button>}
        {detail && <ExportButtons run={exportCsv} />}
      </div>
      {error && <p className="save-status error"><CircleAlert size={15} /> {error}</p>}
    </section>

    {detail && <section className="panel dictionary-detail">
      <div className="panel-heading"><div><h2>{detail.schema}.{detail.name}</h2><p>{detail.purpose || 'Sem descrição.'}</p></div><span className="attention-count">{detail.columnCount} colunas</span></div>
      <div className="table-wrap"><table><thead><tr>
        <th>Coluna</th><th>Tipo</th><th>Nulo</th><th>Chave</th><th>Referência</th><th>Descrição</th>
      </tr></thead><tbody>
        {detail.columns.map((column) => <tr key={column.name}>
          <td className="primary-cell">{column.name}</td>
          <td><code>{column.sqlType}</code><small>{column.clrType}</small></td>
          <td>{column.nullable ? 'Sim' : 'Não'}</td>
          <td>{keyLabel(column)}</td>
          <td>{column.references ?? '—'}</td>
          <td>{column.description || <span className="muted">—</span>}</td>
        </tr>)}
      </tbody></table></div>
    </section>}
  </div>
}
