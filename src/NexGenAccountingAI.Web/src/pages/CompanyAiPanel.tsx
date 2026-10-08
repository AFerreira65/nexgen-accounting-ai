import { ShieldCheck, Sparkles } from 'lucide-react'
import { useEffect, useState } from 'react'
import type { AiSettings } from '../app/demo-data'
import { useWorkContext } from '../app/WorkContext'

const usd = (value: number) => `${value.toLocaleString('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} US$`

/**
 * O interruptor da IA nesta empresa (assistente, explicação de variações, rascunho de listagens e relatórios de gestão).
 * A chave de acesso é uma só, da configuração geral do produtor; aqui decide-se se a IA se usa nesta empresa.
 */
export function CompanyAiPanel() {
  const { activeCompany, source, can, aiSettings, saveAiSettings } = useWorkContext()
  const companyId = activeCompany.companyId
  const [settings, setSettings] = useState<AiSettings | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    if (source !== 'database') { setSettings(null); return }
    void aiSettings(companyId).then(setSettings).catch(() => setSettings(null))
  }, [companyId, source, aiSettings])

  if (source !== 'database' || !settings) return null
  const editable = can('ai.settings')

  async function toggle(patch: Partial<Pick<AiSettings, 'enabled' | 'autoMonthly' | 'anonymizeNames'>> & { retention?: number | null }) {
    if (!settings) return
    setError('')
    try {
      setSettings(await saveAiSettings(companyId, {
        enabled: patch.enabled ?? settings.enabled,
        autoMonthly: patch.autoMonthly ?? settings.autoMonthly,
        anonymizeNames: patch.anonymizeNames ?? settings.anonymizeNames ?? true,
        ...('retention' in patch ? { retentionDays: patch.retention, setRetention: true } : {}),
      }))
    } catch (problem) {
      setError(problem instanceof Error ? problem.message : 'Não foi possível guardar as definições da IA.')
    }
  }

  return <section className="panel settings-panel" aria-label="IA nesta empresa">
    <div className="panel-heading"><div><h2><Sparkles size={17} /> IA nesta empresa</h2><p>Liga o assistente, a explicação de variações, o rascunho de listagens e os relatórios de gestão com IA para {activeCompany.legalName.replace(' (Demonstração)', '')}. Só afeta esta empresa.</p></div></div>
    <div className="settings-grid">
      <label className="toggle-field"><input type="checkbox" checked={settings.enabled} disabled={!editable} onChange={(event) => void toggle({ enabled: event.target.checked })} /><span><strong>Usar a IA nesta empresa</strong><small>A IA só lê os dados desta empresa, com os direitos do utilizador, e o que escreve é sempre para rever.</small></span></label>
      <label className="toggle-field"><input type="checkbox" checked={settings.autoMonthly} disabled={!editable || !settings.enabled} onChange={(event) => void toggle({ autoMonthly: event.target.checked })} /><span><strong>Gerar a síntese mensal automaticamente</strong><small>A partir do dia 10 de cada mês, para o mês anterior.</small></span></label>
    </div>
    <div className="settings-grid">
      <label className="toggle-field"><input type="checkbox" checked={settings.anonymizeNames ?? true} disabled={!editable} onChange={(event) => void toggle({ anonymizeNames: event.target.checked })} /><span><strong><ShieldCheck size={13} /> Esconder nomes e identificadores da IA</strong><small>Os nomes de terceiros, os NIF, os IBAN e os emails vão para a IA trocados por marcadores e voltam repostos na resposta. Reduz o que sai; não é uma garantia absoluta.</small></span></label>
      <label><span>Guardar as conversas com o assistente</span>
        <select aria-label="Prazo de guarda das conversas" disabled={!editable} value={settings.conversationRetentionDays === null || settings.conversationRetentionDays === undefined ? 'forever' : String(settings.conversationRetentionDays)} onChange={(event) => void toggle({ retention: event.target.value === 'forever' ? null : Number(event.target.value) })}>
          <option value="forever">Sem prazo</option><option value="90">90 dias</option><option value="30">30 dias</option><option value="7">7 dias</option><option value="1">1 dia</option><option value="0">Não guardar (cada pergunta é independente)</option>
        </select><small>Ao mudar, apagam-se logo as conversas que passam o prazo. Os relatórios gerados não são conversas e mantêm-se.</small></label>
    </div>
    {settings.budget && <p className="mr-hint" role="status">
      {settings.budget.unlimited
        ? `Consumo de IA este mês: ${usd(settings.budget.spentThisMonthUsd)} (${settings.budget.callsThisMonth} pedidos), sem limite definido.`
        : <>Consumo de IA este mês: <strong>{usd(settings.budget.spentThisMonthUsd)}</strong> de {usd(settings.budget.allowanceUsd ?? 0)}{settings.budget.extraCreditUsd > 0 ? `, mais ${usd(settings.budget.extraCreditUsd)} de crédito extra` : ''}. Resta {usd(settings.budget.availableUsd ?? 0)}.{settings.budget.exhausted ? ' O limite foi atingido: peça mais crédito ao fornecedor do serviço.' : ''}</>}
    </p>}
    {!editable && <p className="mr-hint">O seu grupo de permissões não permite alterar a IA.</p>}
    {error && <p className="save-status error">{error}</p>}
    {!settings.narratorConfigured
      ? <p className="mr-hint">Ainda não há chave de IA: o produtor da aplicação tem de a introduzir na configuração geral («IA — chave da Anthropic»).</p>
      : <p className="mr-hint">Modelo: {settings.model}.</p>}
  </section>
}
