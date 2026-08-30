import { Construction } from 'lucide-react'
import { Link } from 'react-router-dom'

export function PlaceholderPage({ title }: { title: string }) {
  return <div className="page-stack"><header className="page-heading"><div><p className="eyebrow">Accounting AI</p><h1>{title}</h1><p>Este módulo pertence às próximas tarefas da Fase 1.</p></div></header><section className="panel empty-state"><span className="empty-icon"><Construction /></span><h2>Estrutura preparada</h2><p>A experiência visual e a navegação já estão definidas; a funcionalidade será adicionada sem ultrapassar o âmbito atual.</p><Link className="button button-secondary" to="/">Voltar à visão geral</Link></section></div>
}
