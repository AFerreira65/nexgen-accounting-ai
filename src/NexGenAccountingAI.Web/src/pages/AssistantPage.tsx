import { Bot, CircleAlert, Database, MessageSquarePlus, Search, Send, Trash2, User } from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { signalLoadFailure } from '../app/loadFailure'
import { useWorkContext } from '../app/WorkContext'

type ToolUsed = { name: string; summary: string; isError: boolean }
type Message = { id: string; role: 'user' | 'assistant'; content: string; tools: ToolUsed[]; createdAtUtc: string }
type Conversation = { id: string; title: string; updatedAtUtc: string; messages: number }
type Outcome = { refusal: string | null; conversationId: string | null; answer: Message | null; hitLimit: boolean }

const dateTime = new Intl.DateTimeFormat('pt-PT', { dateStyle: 'short', timeStyle: 'short' })

const SUGGESTIONS = [
  'Quanto vendi no mês passado, por cliente?',
  'O que mudou nos gastos entre setembro e agosto?',
  'Há anomalias nos lançamentos deste ano?',
  'O que falta para fechar o mês passado?',
]

/**
 * A conversa com a IA sobre os dados da empresa (F4F-04). A IA responde consultando a contabilidade com
 * ferramentas só de leitura, com os direitos de quem pergunta; a conversa tem memória e cada resposta
 * mostra o que a IA consultou, para se poder conferir.
 */
export function AssistantPage() {
  const { activeCompany, source, user } = useWorkContext()
  const root = `/api/v1/development/users/${user.id}/companies/${activeCompany.companyId}/chat`
  const [conversations, setConversations] = useState<Conversation[]>([])
  const [currentId, setCurrentId] = useState<string | null>(null)
  const [messages, setMessages] = useState<Message[]>([])
  const [question, setQuestion] = useState('')
  const [sending, setSending] = useState(false)
  const [notice, setNotice] = useState('')
  const [error, setError] = useState('')
  const end = useRef<HTMLDivElement>(null)

  const loadList = useCallback(async () => {
    if (source !== 'database') return
    const response = await fetch(root)
    if (!response.ok) { signalLoadFailure(); setError('Não foi possível ler as conversas.'); return }
    setConversations(await response.json() as Conversation[])
  }, [root, source])
  useEffect(() => { void loadList() }, [loadList])
  useEffect(() => { setCurrentId(null); setMessages([]); setNotice('') }, [activeCompany.companyId])
  useEffect(() => { end.current?.scrollIntoView?.({ block: 'end' }) }, [messages, sending])

  async function open(id: string) {
    setNotice(''); setError('')
    const response = await fetch(`${root}/${id}`)
    if (!response.ok) { setError('A conversa já não existe.'); await loadList(); return }
    setCurrentId(id)
    setMessages(await response.json() as Message[])
  }

  function startNew() { setCurrentId(null); setMessages([]); setNotice(''); setError('') }

  async function remove(conversation: Conversation) {
    if (!window.confirm(`Apagar a conversa «${conversation.title}»?`)) return
    const response = await fetch(`${root}/${conversation.id}`, { method: 'DELETE' })
    if (!response.ok) { setError('Não foi possível apagar a conversa.'); return }
    if (currentId === conversation.id) startNew()
    await loadList()
  }

  async function send(text: string) {
    const asked = text.trim()
    if (!asked || sending) return
    setSending(true); setNotice(''); setError(''); setQuestion('')
    setMessages((current) => [...current, { id: `pending-${current.length}`, role: 'user', content: asked, tools: [], createdAtUtc: new Date().toISOString() }])
    try {
      const response = await fetch(root, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ conversationId: currentId, question: asked }) })
      if (!response.ok) { setError('Não foi possível falar com a IA.'); setMessages((current) => current.slice(0, -1)); return }
      const outcome = await response.json() as Outcome
      if (outcome.refusal || !outcome.answer) {
        setNotice(outcome.refusal ?? 'A IA não respondeu.')
        setMessages((current) => current.slice(0, -1))
        setQuestion(asked)
        return
      }

      setCurrentId(outcome.conversationId)
      setMessages((current) => [...current, outcome.answer!])
      await loadList()
    } finally {
      setSending(false)
    }
  }

  if (source !== 'database') {
    return <div className="page-stack"><header className="page-heading"><div><p className="eyebrow">Gestão</p><h1>Assistente</h1><p>A conversa com a IA precisa da base de dados; em demonstração não está disponível.</p></div></header></div>
  }

  return <div className="page-stack">
    <header className="page-heading detail-heading">
      <div><p className="eyebrow">Gestão</p><h1>Assistente</h1><p>Pergunte em português sobre a contabilidade de {activeCompany.legalName}. A IA consulta os dados com ferramentas só de leitura, com os seus direitos, e diz o que consultou.</p></div>
      <span className="connection-badge database"><Database size={15} />Dados da BD</span>
    </header>
    <p className="statement-note">Para responder, a IA recebe os resultados das consultas que faz sobre esta empresa (valores, contas, nomes de terceiros). Só funciona se a IA estiver ligada nas definições da empresa. As respostas podem ter erros: confira os números nas listagens indicadas.</p>
    {error && <p className="save-status error" role="alert"><CircleAlert size={15} /> {error}</p>}

    <div style={{ display: 'grid', gridTemplateColumns: 'minmax(180px, 260px) 1fr', gap: 16, alignItems: 'start' }}>
      <section className="panel" aria-label="Conversas">
        <div className="panel-heading"><h2>Conversas</h2><button type="button" className="icon-button" aria-label="Nova conversa" onClick={startNew}><MessageSquarePlus size={16} /></button></div>
        {conversations.length === 0 && <p className="empty-state">Ainda não há conversas.</p>}
        <ul style={{ listStyle: 'none', margin: 0, padding: 0 }}>
          {conversations.map((conversation) => <li key={conversation.id} style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
            <button type="button" className="link-button" style={{ flex: 1, textAlign: 'left', fontWeight: conversation.id === currentId ? 700 : 400 }} aria-current={conversation.id === currentId} onClick={() => void open(conversation.id)}>
              {conversation.title}<br /><small>{dateTime.format(new Date(conversation.updatedAtUtc))}</small>
            </button>
            <button type="button" className="icon-button" aria-label={`Apagar ${conversation.title}`} onClick={() => void remove(conversation)}><Trash2 size={14} /></button>
          </li>)}
        </ul>
      </section>

      <section className="panel" aria-label="Conversa">
        {messages.length === 0 && <div>
          <p className="empty-state">Escreva uma pergunta ou comece por uma sugestão:</p>
          <div className="filterbar">{SUGGESTIONS.map((suggestion) => <button key={suggestion} type="button" className="button button-secondary" disabled={sending} onClick={() => void send(suggestion)}><Search size={14} /> {suggestion}</button>)}</div>
        </div>}
        <ol style={{ listStyle: 'none', margin: 0, padding: 0, display: 'grid', gap: 12 }}>
          {messages.map((message) => <li key={message.id} aria-label={message.role === 'user' ? 'Mensagem do utilizador' : 'Mensagem do assistente'}>
            <strong>{message.role === 'user' ? <><User size={14} /> Você</> : <><Bot size={14} /> Assistente</>}</strong>
            <p style={{ whiteSpace: 'pre-wrap', margin: '4px 0' }}>{message.content}</p>
            {message.tools.length > 0 && <details>
              <summary><small>A IA consultou {message.tools.length} fonte(s)</small></summary>
              <ul>{message.tools.map((tool, index) => <li key={index}><small>{tool.summary}{tool.isError ? ' (não resultou)' : ''}</small></li>)}</ul>
            </details>}
          </li>)}
        </ol>
        {sending && <p role="status" className="statement-note">A consultar os dados e a escrever a resposta…</p>}
        {notice && <p className="save-status" role="status"><CircleAlert size={15} /> {notice}</p>}
        <div ref={end} />
        <form className="filterbar" onSubmit={(event) => { event.preventDefault(); void send(question) }}>
          <textarea aria-label="Pergunta" rows={2} maxLength={2000} style={{ flex: 1, minWidth: 260 }} value={question} placeholder="Pergunte sobre vendas, gastos, anomalias, fecho…"
            onChange={(event) => setQuestion(event.target.value)}
            onKeyDown={(event) => { if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); void send(question) } }} />
          <button type="submit" className="button button-primary" disabled={sending || question.trim() === ''}><Send size={15} /> Enviar</button>
        </form>
      </section>
    </div>
  </div>
}
