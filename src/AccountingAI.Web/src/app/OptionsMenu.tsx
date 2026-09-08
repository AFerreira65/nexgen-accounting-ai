import { ChevronDown, Database, Info, SlidersHorizontal } from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import type { RecordInfo } from './demo-data'
import { useFocusedRecord } from './RecordContext'
import { screenTableForPath } from './screenTable'
import { useDismiss } from './useDismiss'
import { useWorkContext } from './WorkContext'

const dateTime = new Intl.DateTimeFormat('pt-PT', { dateStyle: 'medium', timeStyle: 'short' })

function when(value: string | null): string {
  if (!value) return '—'
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? '—' : dateTime.format(parsed)
}

function who(party: RecordInfo['createdBy']): string {
  return party?.name?.trim() || 'origem desconhecida'
}

export function OptionsMenu() {
  const { activeCompany, recordInfo, source } = useWorkContext()
  const location = useLocation()
  const navigate = useNavigate()
  const record = useFocusedRecord()
  const wrapRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [aboutOpen, setAboutOpen] = useState(false)
  const [info, setInfo] = useState<RecordInfo | null>(null)
  const [state, setState] = useState<'idle' | 'loading' | 'error'>('idle')

  const close = useCallback(() => { setOpen(false); setAboutOpen(false) }, [])
  useDismiss(open, wrapRef, close)

  // Fecha o painel «Sobre o Registo» ao mudar de ecrã ou de registo em foco.
  useEffect(() => { setAboutOpen(false); setInfo(null); setState('idle') }, [location.pathname, record?.table, record?.id])

  const canAbout = record !== null && source === 'database'

  function openDictionary() {
    const table = screenTableForPath(location.pathname)
    navigate(table ? `/dicionario-de-dados?tabela=${table.schema}/${table.name}` : '/dicionario-de-dados')
    close()
  }

  function toggleAbout() {
    if (!canAbout || !record) return
    const next = !aboutOpen
    setAboutOpen(next)
    if (next && !info && state !== 'loading') {
      setState('loading')
      void recordInfo(activeCompany.companyId, record.table, record.id)
        .then((result) => { setInfo(result); setState('idle') })
        .catch(() => setState('error'))
    }
  }

  return <div className="options-menu-wrap" ref={wrapRef}>
    <button className="period-selector" type="button" aria-expanded={open} aria-haspopup="menu" onClick={() => setOpen((value) => !value)}>
      <SlidersHorizontal size={15} /> Opções <ChevronDown size={15} />
    </button>
    {open && <div className="options-menu" role="menu" aria-label="Opções do ecrã">
      <button type="button" role="menuitem" onClick={openDictionary}>
        <Database size={15} /> Dicionário de dados
      </button>
      <button
        type="button"
        role="menuitem"
        aria-expanded={aboutOpen}
        disabled={!canAbout}
        title={canAbout ? undefined : (source === 'database' ? 'Abra um registo (movimento, conta, diário) para ver quem o criou e alterou.' : 'Requer ligação ao SQL Server.')}
        onClick={toggleAbout}
      >
        <Info size={15} /> Sobre o Registo
      </button>

      {aboutOpen && record && <div className="record-about">
        <p className="record-about-title">{record.label}</p>
        {state === 'loading' && <p className="record-about-line">A carregar…</p>}
        {state === 'error' && <p className="record-about-line error">Não foi possível obter a informação do registo.</p>}
        {state === 'idle' && info && <>
          <p className="record-about-line"><strong>Criado</strong> por {who(info.createdBy)}<br /><small>{when(info.createdAtUtc)}</small></p>
          <p className="record-about-line">
            {info.modifiedAtUtc
              ? <><strong>Última alteração</strong> por {who(info.modifiedBy)}<br /><small>{when(info.modifiedAtUtc)}</small></>
              : <><strong>Sem alterações</strong> registadas desde a criação.</>}
          </p>
        </>}
      </div>}
    </div>}
  </div>
}
