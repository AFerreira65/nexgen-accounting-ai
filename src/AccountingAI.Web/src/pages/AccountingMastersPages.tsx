import { BookOpen, CircleAlert, CircleCheck, FileText, Landmark, Search, Tags } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { useWorkContext } from '../app/WorkContext'

function useFiltered<T>(items: T[], text: string, value: (item: T) => string) {
  return useMemo(() => {
    const normalized = text.trim().toLocaleLowerCase('pt-PT')
    return normalized ? items.filter((item) => value(item).toLocaleLowerCase('pt-PT').includes(normalized)) : items
  }, [items, text, value])
}

function SourceBadge({ database }: { database: boolean }) {
  return <span className={`connection-badge ${database ? 'database' : 'demonstration'}`}>{database ? 'Dados da BD' : 'Dados de demonstração'}</span>
}

export function AccountsPage() {
  const { accountingSetup, setupSource, setupLoading } = useWorkContext()
  const [search, setSearch] = useState('')
  const accounts = useFiltered(accountingSetup.accounts, search, (item) => `${item.code} ${item.name} ${item.taxonomyCode ?? ''}`)
  const posting = accountingSetup.accounts.filter((item) => item.allowsPosting)
  const classified = posting.filter((item) => item.taxonomyCode !== null)

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Cadastros contabilísticos</p><h1>Plano e abertura de contas</h1><p>Estrutura própria da empresa, criada a partir do referencial {accountingSetup.template.code}.</p></div><SourceBadge database={setupSource === 'database'} /></header>
    <section className="metric-grid compact-grid"><article className="metric-card"><span className="metric-icon teal"><BookOpen /></span><div><small>Contas ativas</small><strong>{accountingSetup.accounts.filter((item) => item.isActive).length}</strong><span>{posting.length} contas de movimento</span></div></article><article className="metric-card"><span className="metric-icon blue"><Tags /></span><div><small>Taxonomias atribuídas</small><strong>{classified.length}</strong><span>Referencial {accountingSetup.template.taxonomyReference}</span></div></article></section>
    <div className="filterbar"><label><Search size={17} /><span className="sr-only">Pesquisar contas</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar código, descrição ou taxonomia…" /></label><span className="catalog-count">{setupLoading ? 'A carregar…' : `${accounts.length} contas`}</span></div>
    <section className="panel"><div className="panel-heading"><div><h2>Contas da empresa</h2><p>As contas agregadoras não podem receber movimentos nem taxonomia.</p></div></div><div className="table-wrap"><table><thead><tr><th>Conta</th><th>Descrição</th><th>Agregadora</th><th>Tipo</th><th>Taxonomia</th><th>Estado</th></tr></thead><tbody>{accounts.map((account) => <tr key={account.code}><td><Link className="primary-cell" to={`/contas/${account.code}`}>{account.code}</Link></td><td>{account.name}<small>{account.sourceTemplateVersion ? 'Plano base' : 'Configuração da empresa'}</small></td><td>{account.parentCode ?? '—'}</td><td>{account.allowsPosting ? 'Movimento' : 'Agregadora'}</td><td>{account.taxonomyCode ? <span className="taxonomy-pill">{account.taxonomyReference} · {account.taxonomyCode}</span> : <span className="muted-value">Por atribuir</span>}</td><td><span className={`badge ${account.isActive ? 'success' : 'warning'}`}>{account.isActive ? 'Ativa' : 'Inativa'}</span></td></tr>)}</tbody></table></div></section>
  </div>
}

export function JournalsPage() {
  const { accountingSetup, movements, setupSource } = useWorkContext()
  const [search, setSearch] = useState('')
  const journals = useFiltered(accountingSetup.journals, search, (item) => `${item.code} ${item.name}`)

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Cadastros contabilísticos</p><h1>Diários</h1><p>Diários operacionais da empresa e respetiva utilização nos movimentos.</p></div><SourceBadge database={setupSource === 'database'} /></header>
    <div className="filterbar"><label><Search size={17} /><span className="sr-only">Pesquisar diários</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar código ou descrição…" /></label><span className="catalog-count">{journals.length} diários</span></div>
    <section className="panel"><div className="table-wrap"><table><thead><tr><th>Diário</th><th>Descrição</th><th className="numeric">Movimentos</th><th>Origem</th><th>Estado</th></tr></thead><tbody>{journals.map((journal) => <tr key={journal.code}><td><Link className="inline-link" to={`/diarios/${journal.code}`}><Landmark size={14} /> {journal.code}</Link></td><td>{journal.name}</td><td className="numeric strong">{movements.filter((item) => item.journalId === journal.code).length}</td><td>{journal.sourceTemplateVersion ? 'Tabela predefinida' : 'Empresa'}</td><td><span className={`badge ${journal.isActive ? 'success' : 'warning'}`}>{journal.isActive ? 'Ativo' : 'Inativo'}</span></td></tr>)}</tbody></table></div></section>
  </div>
}

export function DocumentTypesPage() {
  const { accountingSetup, setupSource } = useWorkContext()
  const [search, setSearch] = useState('')
  const documents = useFiltered(accountingSetup.documentTypes, search, (item) => `${item.code} ${item.name} ${item.defaultJournalCode}`)

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Cadastros contabilísticos</p><h1>Documentos contabilísticos</h1><p>Tipos de documento e diário sugerido para o lançamento.</p></div><SourceBadge database={setupSource === 'database'} /></header>
    <div className="filterbar"><label><Search size={17} /><span className="sr-only">Pesquisar documentos</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar documento ou diário…" /></label><span className="catalog-count">{documents.length} documentos</span></div>
    <section className="panel"><div className="table-wrap"><table><thead><tr><th>Documento</th><th>Descrição</th><th>Diário predefinido</th><th>Origem</th><th>Estado</th></tr></thead><tbody>{documents.map((document) => <tr key={document.code}><td><span className="inline-link"><FileText size={14} /> {document.code}</span></td><td>{document.name}</td><td><Link className="inline-link" to={`/diarios/${document.defaultJournalCode}`}>{document.defaultJournalCode}</Link></td><td>{document.sourceTemplateVersion ? 'Tabela predefinida' : 'Empresa'}</td><td><span className={`badge ${document.isActive ? 'success' : 'warning'}`}>{document.isActive ? 'Ativo' : 'Inativo'}</span></td></tr>)}</tbody></table></div></section>
  </div>
}

export function TaxonomiesPage() {
  const { accountingSetup, setupSource } = useWorkContext()
  const [search, setSearch] = useState('')
  const posting = accountingSetup.accounts.filter((item) => item.allowsPosting)
  const accounts = useFiltered(posting, search, (item) => `${item.code} ${item.name} ${item.taxonomyCode ?? ''}`)
  const classified = posting.filter((item) => item.taxonomyCode !== null)
  const missing = posting.length - classified.length

  return <div className="page-stack">
    <header className="page-heading"><div><p className="eyebrow">Conformidade SAF-T</p><h1>Taxonomias</h1><p>{accountingSetup.template.taxonomyName}, determinada pela norma {accountingSetup.template.name}.</p></div><SourceBadge database={setupSource === 'database'} /></header>
    <section className="taxonomy-summary panel"><div className="taxonomy-reference"><span className="heading-symbol"><Tags /></span><div><small>TaxonomyReference</small><strong>{accountingSetup.template.taxonomyReference}</strong><span>{accountingSetup.template.taxonomyName}</span></div></div><div className="taxonomy-health"><span className={missing ? 'warning' : 'success'}>{missing ? <CircleAlert /> : <CircleCheck />}</span><div><small>Cobertura atual</small><strong>{classified.length} de {posting.length}</strong><span>{missing ? `${missing} contas de movimento por classificar` : 'Todas as contas de movimento classificadas'}</span></div></div></section>
    <div className="filterbar"><label><Search size={17} /><span className="sr-only">Pesquisar taxonomias</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Pesquisar conta ou código de taxonomia…" /></label><a className="button button-secondary" href={accountingSetup.template.taxonomyLegalSource || undefined} target="_blank" rel="noreferrer">Fonte oficial</a></div>
    <section className="panel"><div className="panel-heading"><div><h2>Classificação das contas de movimento</h2><p>O código é obrigatório no SAF-T apenas para contas classificadas como movimento.</p></div></div><div className="table-wrap"><table><thead><tr><th>Conta</th><th>Descrição</th><th>Referencial</th><th>Código</th><th>Validação</th></tr></thead><tbody>{accounts.map((account) => <tr key={account.code}><td><Link className="primary-cell" to={`/contas/${account.code}`}>{account.code}</Link></td><td>{account.name}</td><td>{account.taxonomyReference ?? accountingSetup.template.taxonomyReference}</td><td>{account.taxonomyCode ?? '—'}</td><td>{account.taxonomyCode ? <span className="badge success">Atribuída</span> : <span className="badge warning">Por atribuir</span>}</td></tr>)}</tbody></table></div></section>
  </div>
}
