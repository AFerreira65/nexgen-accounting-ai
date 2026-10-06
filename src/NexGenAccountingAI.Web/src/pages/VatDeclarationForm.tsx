import { useEffect, useState } from 'react'
import type { VatDeclaration, VatFormField } from '../app/demo-data'

/** O que se preenche à mão no impresso (espelho de `VatDeclarationInputs`). */
export type VatDeclarationInputs = {
  withinDeadline: boolean
  filedRecapitulativeStatement: boolean
  noOperations: boolean
  operationsAsAcquirer: boolean | null
  fields: Record<string, number>
  certifiedAccountantTaxNumber: string | null
}

export const EMPTY_INPUTS: VatDeclarationInputs = { withinDeadline: true, filedRecapitulativeStatement: false, noOperations: false, operationsAsAcquirer: null, fields: {}, certifiedAccountantTaxNumber: null }

const amount = new Intl.NumberFormat('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2, useGrouping: 'always' })
const SEATS = [['Continente', 'CONTINENTE'], ['Acores', 'AÇORES'], ['Madeira', 'MADEIRA']] as const

type Props = {
  declaration: VatDeclaration
  inputs: VatDeclarationInputs
  companyName: string
  taxNumber: string
  readOnly?: boolean
  onChange: (next: VatDeclarationInputs) => void
  onInspect?: (field: VatFormField) => void
  inspecting?: number | null
}

function periodParts(declaration: VatDeclaration) {
  const year = declaration.to.slice(0, 4)
  return { year, period: declaration.period ?? `${declaration.from.slice(5, 7)}–${declaration.to.slice(5, 7)}` }
}

/**
 * A declaração periódica de IVA com o aspeto do impresso da AT (F4C-01): os quadros
 * 01 a 06, 06-A e 20, com os números dos campos. Os campos apurados pela contabilidade
 * aparecem a cinzento e abrem as contas que os formam; os que a aplicação não apura
 * escrevem-se aqui e ficam guardados por período.
 */
export function VatDeclarationForm({ declaration, inputs, companyName, taxNumber, readOnly = false, onChange, onInspect, inspecting }: Props) {
  const { year, period } = periodParts(declaration)
  const byNumber = new Map(declaration.fields.map((field) => [field.number, field]))
  const seat = declaration.seat === 'Açores' ? 'Acores' : declaration.seat
  const acquirer = inputs.operationsAsAcquirer ?? (byNumber.get(106)?.amount ?? 0) !== 0

  function setField(number: number, raw: string) {
    const fields = { ...inputs.fields }
    const value = Number(raw.replace(/\s/g, '').replace(',', '.'))
    if (raw.trim() === '' || !Number.isFinite(value) || value === 0) delete fields[number]
    else fields[number] = value
    onChange({ ...inputs, fields })
  }

  // Um campo do quadro: calculado (cinzento, com as contas) ou à mão (editável).
  const Box = ({ number, total = false }: { number: number; total?: boolean }) => {
    const field = byNumber.get(number)
    if (!field) return <span className="atf-cell" />
    const manual = !field.supported && !field.computed
    return <span className={`atf-cell ${total ? 'atf-total' : ''} ${inspecting === number ? 'atf-open' : ''}`}>
      <span className="atf-num">{number}</span>
      {manual
        ? <ManualInput number={number} value={inputs.fields[number]} disabled={readOnly || inputs.noOperations} onCommit={(raw) => setField(number, raw)} />
        : <button type="button" className="atf-value" disabled={!onInspect || field.sources.length === 0} title={field.sources.length > 0 ? 'Ver as contas que formam o campo' : undefined} onClick={() => onInspect?.(field)}>
          {amount.format(field.amount)}
        </button>}
    </span>
  }

  const Check = ({ checked, label, onToggle }: { checked: boolean; label: string; onToggle?: () => void }) =>
    <label className="atf-check"><input type="checkbox" checked={checked} disabled={readOnly || !onToggle} onChange={onToggle} aria-label={label} /><span>{label}</span></label>

  return <div className="atf" aria-label="Declaração periódica de IVA — impresso">
    <div className="atf-head">
      <div className="atf-brand"><strong>AT</strong><span>autoridade tributária e aduaneira</span></div>
      <div className="atf-title">DECLARAÇÃO PERIÓDICA</div>
      <div className="atf-tax"><strong>IVA</strong><span>IMPOSTO SOBRE O VALOR ACRESCENTADO</span></div>
    </div>

    <section className="atf-quadro">
      <header><span className="atf-qn">01</span><span>IDENTIFICAÇÃO</span></header>
      <div className="atf-row atf-q01">
        <span className="atf-label">Do Sujeito Passivo</span>
        <span className="atf-field"><small>Número de Identificação Fiscal</small><span className="atf-input atf-static">{taxNumber}</span></span>
        <span className="atf-field"><small>Nome</small><span className="atf-input atf-static">{companyName}</span></span>
        <span className="atf-field"><small>Localização da sede</small><span className="atf-checks">{SEATS.map(([key, label]) => <Check key={key} checked={seat === key} label={label} />)}</span></span>
      </div>
    </section>

    <div className="atf-grid3">
      <section className="atf-quadro">
        <header><span className="atf-qn">02</span><span>DA DECLARAÇÃO</span></header>
        <div className="atf-row">
          <span className="atf-field"><small>Ano</small><span className="atf-input atf-static">{year}</span></span>
          <span className="atf-field"><small>Período declarativo</small><span className="atf-input atf-static">{period}</span></span>
          <span className="atf-checks atf-vertical">
            <Check checked={inputs.withinDeadline} label="Dentro do prazo" onToggle={() => onChange({ ...inputs, withinDeadline: true })} />
            <Check checked={!inputs.withinDeadline} label="Fora do prazo" onToggle={() => onChange({ ...inputs, withinDeadline: false })} />
          </span>
        </div>
      </section>
      <section className="atf-quadro">
        <header><span className="atf-qn">03</span><span>OPERAÇÕES EM ESPAÇO DIFERENTE DO DA SEDE</span></header>
        <div className="atf-row"><span className="atf-checks">{SEATS.map(([key, label]) => <Check key={key} checked={false} label={label} />)}</span><small className="atf-hint">Anexo R: ainda não preenchido pela aplicação (ver campos 65 a 68).</small></div>
      </section>
      <section className="atf-quadro">
        <header><span className="atf-qn">04</span><span>DECLARAÇÃO RECAPITULATIVA</span></header>
        <div className="atf-row"><Check checked={inputs.filedRecapitulativeStatement} label="Assinale se no período de referência apresentou alguma declaração recapitulativa" onToggle={() => onChange({ ...inputs, filedRecapitulativeStatement: !inputs.filedRecapitulativeStatement })} /></div>
      </section>
    </div>

    <section className="atf-quadro">
      <header><span className="atf-qn">05</span><span>INEXISTÊNCIA DE OPERAÇÕES</span></header>
      <div className="atf-row"><Check checked={inputs.noOperations} label="Assinale se, no período a que respeita a declaração, não realizou operações ativas nem passivas que devam constar do Quadro 06." onToggle={() => onChange({ ...inputs, noOperations: !inputs.noOperations })} /></div>
    </section>

    <section className={`atf-quadro ${inputs.noOperations ? 'atf-muted' : ''}`}>
      <header><span className="atf-qn">06</span><span>APURAMENTO DO IMPOSTO RESPEITANTE AO PERÍODO A QUE A DECLARAÇÃO SE REFERE</span></header>
      <div className="atf-row atf-question">
        <span><strong>EFETUOU OPERAÇÕES DESTA NATUREZA?</strong> <small>(valores incluídos nos campos 1, 5, 3 ou 9) — em que, na qualidade de adquirente, liquidou o imposto; alíneas a), b) e c) do artigo 42.º; alíneas f) e g) do n.º 3 do artigo 3.º e a) e b) do n.º 2 do artigo 4.º do CIVA</small></span>
        <span className="atf-checks">
          <Check checked={acquirer} label="SIM (preencha também o Quadro 06-A)" onToggle={() => onChange({ ...inputs, operationsAsAcquirer: true })} />
          <Check checked={!acquirer} label="NÃO" onToggle={() => onChange({ ...inputs, operationsAsAcquirer: false })} />
        </span>
      </div>
      <table className="atf-table">
        <thead><tr><th /><th>BASE TRIBUTÁVEL</th><th>IMPOSTO A FAVOR DO SUJEITO PASSIVO</th><th>IMPOSTO A FAVOR DO ESTADO</th></tr></thead>
        <tbody>
          <tr className="atf-group"><td colSpan={4}>1 — TRANSMISSÕES DE BENS E PRESTAÇÕES DE SERVIÇOS EM QUE LIQUIDOU IMPOSTO</td></tr>
          <tr><td>À taxa reduzida</td><td><Box number={1} /></td><td /><td><Box number={2} /></td></tr>
          <tr><td>À taxa intermédia</td><td><Box number={5} /></td><td /><td><Box number={6} /></td></tr>
          <tr><td>À taxa normal</td><td><Box number={3} /></td><td /><td><Box number={4} /></td></tr>
          <tr><td className="atf-indent">Isentas ou não tributadas — transmissões intracomunitárias de bens e prestações de serviços mencionadas nas declarações recapitulativas</td><td><Box number={7} /></td><td /><td /></tr>
          <tr><td className="atf-indent">Isentas ou não tributadas — que conferem direito à dedução</td><td><Box number={8} /></td><td /><td /></tr>
          <tr><td className="atf-indent">Isentas ou não tributadas — que não conferem direito à dedução</td><td><Box number={9} /></td><td /><td /></tr>
          <tr className="atf-group"><td colSpan={4}>2 — AQUISIÇÕES INTRACOMUNITÁRIAS DE BENS E OPERAÇÕES ASSIMILADAS</td></tr>
          <tr><td>Total (10 = 12 + 14 + 15) · (11 = 13)</td><td><Box number={10} total /></td><td /><td><Box number={11} total /></td></tr>
          <tr><td className="atf-indent">Cujo imposto foi liquidado pelo declarante</td><td><Box number={12} /></td><td /><td><Box number={13} /></td></tr>
          <tr><td className="atf-indent">Abrangidas pelos artigos 15.º do CIVA ou do RITI</td><td><Box number={14} /></td><td /><td /></tr>
          <tr><td className="atf-indent">Abrangidas pelos n.ºs 3, 4 e 5 do artigo 22.º do RITI</td><td><Box number={15} /></td><td /><td /></tr>
          <tr className="atf-group"><td colSpan={4}>3 — PRESTAÇÕES DE SERVIÇOS EFETUADAS POR SUJEITOS PASSIVOS DE OUTROS ESTADOS MEMBROS, CUJO IMPOSTO FOI LIQUIDADO PELO DECLARANTE</td></tr>
          <tr><td /><td><Box number={16} /></td><td /><td><Box number={17} /></td></tr>
          <tr className="atf-group"><td colSpan={4}>4 — IMPORTAÇÕES DE BENS CUJO IMPOSTO FOI LIQUIDADO PELO DECLARANTE (n.º 8 do artigo 27.º do CIVA)</td></tr>
          <tr><td /><td><Box number={18} /></td><td /><td><Box number={19} /></td></tr>
          <tr className="atf-group"><td colSpan={4}>5 — IMPOSTO DEDUTÍVEL</td></tr>
          <tr><td>Ativos não correntes (imobilizado)</td><td /><td><Box number={20} /></td><td /></tr>
          <tr><td className="atf-indent">Inventários (existências) — à taxa reduzida</td><td /><td><Box number={21} /></td><td /></tr>
          <tr><td className="atf-indent">Inventários (existências) — à taxa intermédia</td><td /><td><Box number={23} /></td><td /></tr>
          <tr><td className="atf-indent">Inventários (existências) — à taxa normal</td><td /><td><Box number={22} /></td><td /></tr>
          <tr><td>Outros bens e serviços</td><td /><td><Box number={24} /></td><td /></tr>
          <tr className="atf-group"><td colSpan={4}>6 — REGULARIZAÇÕES MENSAIS, TRIMESTRAIS E ANUAIS</td></tr>
          <tr><td /><td /><td><Box number={40} /></td><td><Box number={41} /></td></tr>
          <tr className="atf-group"><td colSpan={4}>7 — EXCESSO A REPORTAR DO PERÍODO ANTERIOR (campo 96 da declaração anterior — n.º 4 do artigo 22.º)</td></tr>
          <tr><td /><td /><td><Box number={61} /></td><td /></tr>
          <tr className="atf-group"><td colSpan={4}>8 — ANEXO (ver Quadro 03) · campos 63 e 64 do Quadro 06 do Anexo R</td></tr>
          <tr><td /><td /><td><Box number={65} /></td><td><Box number={66} /></td></tr>
          <tr className="atf-group"><td colSpan={4}>9 — ANEXO (ver Quadro 03) · campos 63 e 64 do Quadro 06 do Anexo R</td></tr>
          <tr><td /><td /><td><Box number={67} /></td><td><Box number={68} /></td></tr>
          <tr className="atf-totals"><td>TOTAIS</td><td><small>Total da base tributável (1+5+3+…+10+16+18)</small><Box number={90} total /></td><td><small>Total do imposto a favor do sujeito passivo (20+21+…+67)</small><Box number={91} total /></td><td><small>Total do imposto a favor do Estado (2+6+4+11+17+…+68)</small><Box number={92} total /></td></tr>
        </tbody>
      </table>
      <div className="atf-settlement">
        <div><strong>IMPOSTO A ENTREGAR AO ESTADO</strong><Box number={93} total /><small>(92 − 91)</small></div>
        <div><strong>CRÉDITO A RECUPERAR</strong><Box number={94} total /><small>(91 − 92)</small></div>
        <div><strong>SOLICITO REEMBOLSO</strong><Box number={95} /></div>
        <div><strong>EXCESSO A REPORTAR</strong><Box number={96} total /><small>Valor a inscrever no campo 61 da declaração do período seguinte.</small></div>
      </div>
      <p className="atf-attention">ATENÇÃO: a opção pelo pedido de reembolso veda a possibilidade de utilizar o respetivo valor em declarações seguintes como «excesso a reportar», salvo comunicação em contrário da DSR (situações de indeferimento de reembolso).</p>
    </section>

    <section className={`atf-quadro ${inputs.noOperations || !acquirer ? 'atf-muted' : ''}`}>
      <header><span className="atf-qn">06-A</span><span>DESENVOLVIMENTO DO QUADRO 06</span></header>
      <div className="atf-sub">A — OPERAÇÕES LOCALIZADAS EM PORTUGAL EM QUE, NA QUALIDADE DE ADQUIRENTE, LIQUIDOU O IVA DEVIDO <small>(valores das bases tributáveis, incluídos nos campos 1, 5 e 3)</small></div>
      <div className="atf-pairs">
        <div><span>Efetuadas por entidades residentes em países comunitários <small>(não inclui as operações mencionadas no campo 16)</small></span><Box number={97} /></div>
        <div><span>Efetuadas por entidades residentes em países ou territórios terceiros</span><Box number={98} /></div>
      </div>
      <div className="atf-sub">B — OPERAÇÕES EM QUE LIQUIDOU O IVA DEVIDO POR APLICAÇÃO DA REGRA DE INVERSÃO DO SUJEITO PASSIVO <small>(valores das bases tributáveis, incluídos nos campos 1, 5 e 3)</small></div>
      <div className="atf-pairs atf-two">
        <div><span><b>Ouro</b> <small>(Decreto-Lei 362/99)</small></span><Box number={99} /></div>
        <div><span><b>Aquisição de imóveis com renúncia à isenção</b> <small>(Decreto-Lei 21/2007)</small></span><Box number={100} /></div>
        <div><span><b>Sucatas</b> <small>[alínea i) do n.º 1 do artigo 2.º do CIVA]</small></span><Box number={101} /></div>
        <div><span><b>Serviços de construção civil</b> <small>[alínea j) do n.º 1 do artigo 2.º do CIVA]</small></span><Box number={102} /></div>
        <div><span><b>Emissão de gases com efeito de estufa</b> <small>[alínea l) do n.º 1 do artigo 2.º do CIVA]</small></span><Box number={105} /></div>
        <div><span><b>Aquisição de cortiça e outros produtos de origem silvícola</b> <small>[alínea m) do n.º 1 do artigo 2.º do CIVA]</small></span><Box number={107} /></div>
      </div>
      <div className="atf-sub">C — OPERAÇÕES REFERIDAS NAS ALÍNEAS F) E G) DO N.º 3 DO ARTIGO 3.º E ALÍNEAS A) E B) DO N.º 2 DO ARTIGO 4.º DO CIVA <small>(valores das bases tributáveis, incluídos nos campos 1, 5 e 3)</small></div>
      <div className="atf-pairs"><div><span>Se efetuou operações desta natureza, indique o seu valor</span><Box number={103} /></div></div>
      <div className="atf-sub">D — OPERAÇÕES REFERIDAS NAS ALÍNEAS A), B) E C) DO ARTIGO 42.º DO CIVA <small>(valores das bases tributáveis, incluídos nos campos 1, 5, 3 e 9)</small></div>
      <div className="atf-pairs"><div><span>Se efetuou operações desta natureza, indique o seu valor</span><Box number={104} /></div></div>
      <div className="atf-pairs atf-sum"><div><span><strong>SOMA DO QUADRO 06-A</strong> (97 + … + 105 + 107)</span><Box number={106} total /></div></div>
    </section>

    <section className="atf-quadro">
      <header><span className="atf-qn">20</span><span>ZONA PARA IDENTIFICAÇÃO DO CONTABILISTA CERTIFICADO, NOS CASOS EM QUE SEJA OBRIGATÓRIA</span></header>
      <div className="atf-row">
        <span className="atf-field"><small>Número de Identificação Fiscal</small>
          <input className="atf-input atf-nif" aria-label="NIF do contabilista certificado" inputMode="numeric" maxLength={9} disabled={readOnly} defaultValue={inputs.certifiedAccountantTaxNumber ?? ''} key={inputs.certifiedAccountantTaxNumber ?? ''} onBlur={(event) => { const next = event.target.value.trim() || null; if (next !== inputs.certifiedAccountantTaxNumber) onChange({ ...inputs, certifiedAccountantTaxNumber: next }) }} />
        </span>
        <small className="atf-hint">O justo impedimento (artigo 12.º-A do Estatuto da OCC) não é preenchido pela aplicação.</small>
      </div>
    </section>

    <p className="atf-foot"><strong>A PRESENTE DECLARAÇÃO CORRESPONDE À VERDADE E NÃO OMITE QUALQUER INFORMAÇÃO PEDIDA.</strong></p>
  </div>
}

/** Um campo à mão: edita-se como texto e só se grava ao sair, para não disparar um cálculo por tecla. */
function ManualInput({ number, value, disabled, onCommit }: { number: number; value: number | undefined; disabled: boolean; onCommit: (raw: string) => void }) {
  const [text, setText] = useState(value === undefined ? '' : amount.format(value))
  useEffect(() => { setText(value === undefined ? '' : amount.format(value)) }, [value])
  return <input
    className="atf-value atf-manual"
    aria-label={`Campo ${number}`}
    inputMode="decimal"
    placeholder="à mão"
    disabled={disabled}
    value={text}
    onChange={(event) => setText(event.target.value)}
    onBlur={() => { const current = value === undefined ? '' : amount.format(value); if (text !== current) onCommit(text) }}
    onKeyDown={(event) => { if (event.key === 'Enter') (event.target as HTMLInputElement).blur() }}
  />
}
