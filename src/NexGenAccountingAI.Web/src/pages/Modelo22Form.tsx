import type { Modelo22Return } from '../app/demo-data'

const amount = new Intl.NumberFormat('pt-PT', { minimumFractionDigits: 2, maximumFractionDigits: 2, useGrouping: 'always' })

/** A ordem oficial dos campos do quadro 07 (impresso em vigor a partir de janeiro de 2026). */
const VARIACOES = [702, 703, 805, 704, 705, 806, 706, 707]
const ACRESCER = [709, 710, 711, 782, 712, 713, 714, 715, 717, 721, 724, 725, 716, 731, 726, 783, 728, 727, 729, 730, 732, 733, 784, 734, 735, 780, 785, 802, 746, 737, 786, 718, 719, 720, 722, 723, 736, 738, 739, 740, 741, 742, 743, 787, 744, 745, 747, 748, 749, 788, 750, 789, 790, 751, 803, 779, 797, 799, 804, 752]
const DEDUZIR = [754, 755, 756, 757, 791, 758, 759, 760, 761, 762, 763, 781, 764, 765, 766, 792, 767, 768, 769, 770, 793, 771, 794, 772, 795, 773, 796, 774, 800, 801, 798, 775]

const TIPOS_SUJEITO = ['Residente que exerce, a título principal, atividade comercial, industrial ou agrícola', 'Residente que não exerce, a título principal, atividade comercial, industrial ou agrícola', 'Não residente com estabelecimento estável', 'Não residente sem estabelecimento estável']
const TIPOS_DECLARACAO = ['1.ª Declaração do período', 'Declaração de substituição (art.º 122.º, n.ºs 1 e 2)', 'Declaração de substituição (art.º 64.º, n.º 4)', 'Declaração de substituição (art.º 120.º, n.ºs 8 e 9)', 'Declaração de substituição (art.º 64.º, n.º 4) fora do prazo legal', 'Declaração de substituição (art.º 122.º, n.º 3)']
const REDUCAO_TAXA: [number, string, string][] = [
  [242, 'Estabelecimentos de ensino particular (ex-art.º 56.º do EBF)', '20%'],
  [245, 'Benefícios fiscais aplicáveis aos territórios do Interior (art.º 41.º-B e ex-art.º 43.º do EBF)', '12,5/20%'],
  [269, 'Benefícios fiscais aplicáveis aos territórios do interior (R.A. Madeira)', '8,75/14%'],
  [270, 'Benefícios fiscais aplicáveis aos territórios do interior (R.A. Açores)', '8,75/14%'],
  [248, 'Antigo Estatuto Fiscal Cooperativo (art.º 7.º, n.º 3 da Lei n.º 85/98)', '20%'],
  [260, 'Entidades licenciadas na Zona Franca da Madeira (ex-art.º 35.º do EBF)', '3%'],
  [265, 'Entidades licenciadas na Zona Franca da Madeira (art.ºs 36.º e 36.º-A do EBF)', '5%'],
]
const REGIME_GERAL: [number, string, string][] = [
  [246, 'Região Autónoma dos Açores (Dec. Leg. Regional n.º 2/1999/A)', '8,75%/11,2%/14%'],
  [249, 'Região Autónoma da Madeira (Dec. Leg. Regional n.º 2/2001/M)', '8,75%/11,2%/14%'],
  [262, 'Rendimentos prediais de entidades não residentes sem estabelecimento estável (art.º 87.º, n.º 4)', '25%'],
  [263, 'Mais-valias imobiliárias / incrementos patrimoniais obtidos por entidades não residentes sem estabelecimento estável (art.º 87.º, n.º 4)', '25%'],
  [266, 'Mais-valias mobiliárias obtidas por entidades não residentes sem estabelecimento estável (art.º 87.º, n.º 4)', '25%'],
  [267, 'Rendimentos de unidades de participação em FII e participações sociais em SII, auferidos por não residentes (art.º 22.º-A do EBF)', '10%'],
  [268, 'Rendimentos de capitais não sujeitos a retenção na fonte a título definitivo', '5% / 10% / 12% / 15% / 25% / 35%'],
  [264, 'Outros rendimentos obtidos por entidades não residentes sem estabelecimento estável e não sujeitos a retenções na fonte a título definitivo', '5% / 10% / 12% / 15% / 25%'],
]

type Props = {
  result: Modelo22Return
  draft: Record<string, string>
  onDraft: (code: string, text: string) => void
  companyName: string
  taxNumber: string
  sme: boolean
  onSme: (value: boolean) => void
  municipalRate: string
  onMunicipalRate: (value: string) => void
}

/**
 * O Modelo 22 com o aspeto do impresso da AT (F4C-02): rosto (quadros 01 a 05), 07, 08,
 * 09, 10 e 11. Os campos calculados pela aplicação aparecem a cinzento; os restantes
 * escrevem-se no impresso e gravam-se com «Guardar e recalcular».
 */
export function Modelo22Form({ result, draft, onDraft, companyName, taxNumber, sme, onSme, municipalRate, onMunicipalRate }: Props) {
  const byCode = new Map(result.fields.map((field) => [field.code, field]))
  // O campo 303 do impresso é o saldo de prejuízos reportáveis que a aplicação transporta.
  if (!byCode.has('303') && byCode.has('prejuizos-disponiveis')) byCode.set('303', { ...byCode.get('prejuizos-disponiveis')!, code: '303', computed: true })
  const pick = (code: string) => Number(draft[code] ?? byCode.get(code)?.amount ?? 0)
  const setPick = (code: string, value: number) => onDraft(code, pick(code) === value ? '' : String(value))

  // Um campo do impresso: calculado (cinzento) ou à mão (editável, com a sugestão da contabilidade).
  const F = ({ code, total = false, wide = false }: { code: string; total?: boolean; wide?: boolean }) => {
    const field = byCode.get(code)
    if (!field) return <span className={`atf-cell ${wide ? 'atf-wide' : ''}`}><span className="atf-num">{code}</span><span className="atf-value atf-blank" /></span>
    return <span className={`atf-cell ${total ? 'atf-total' : ''} ${wide ? 'atf-wide' : ''}`}>
      <span className="atf-num">{code}</span>
      {field.computed
        ? <span className="atf-value">{amount.format(field.amount)}</span>
        : <input className="atf-value atf-manual" aria-label={`Campo ${code}`} inputMode="decimal" placeholder="0,00" value={draft[code] ?? ''} onChange={(event) => onDraft(code, event.target.value)} />}
      {!field.computed && field.suggested !== null && !(draft[code] ?? '') && <button type="button" className="atf-suggest" title={field.suggestionSource ?? undefined} onClick={() => onDraft(code, String(field.suggested))}>usar {amount.format(field.suggested)}</button>}
    </span>
  }
  const Row = ({ code, label }: { code: string; label?: string }) => {
    const field = byCode.get(code)
    return <tr className={field?.computed ? 'atf-computed' : ''}><td>{label ?? field?.label ?? code}</td><td><F code={code} total={field?.computed} /></td></tr>
  }
  const Check = ({ code, value, label }: { code: string; value: number; label: string }) =>
    <label className="atf-check"><span className="atf-num atf-num-box">{value}</span><input type="checkbox" checked={pick(code) === value} onChange={() => setPick(code, value)} aria-label={label} /><span>{label}</span></label>
  const Nif = ({ code, label }: { code: string; label: string }) =>
    <span className="atf-field"><small>{label}</small><input className="atf-input atf-nif" aria-label={label} inputMode="numeric" maxLength={9} value={draft[code] ?? ''} onChange={(event) => onDraft(code, event.target.value.replace(/\D/g, ''))} /></span>

  return <div className="atf atf-irc" aria-label="Modelo 22 — impresso">
    <div className="atf-head atf-irc-head">
      <div className="atf-irc-title"><span>MINISTÉRIO DAS FINANÇAS</span><span>AUTORIDADE TRIBUTÁRIA E ADUANEIRA</span><strong>DECLARAÇÃO DE RENDIMENTOS</strong></div>
      <div className="atf-irc-period">
        <section className="atf-quadro"><header><span className="atf-qn">01</span><span>PERÍODO DE TRIBUTAÇÃO</span></header>
          <div className="atf-row"><span className="atf-field"><small>De</small><span className="atf-input atf-static">{result.year}-01-01</span></span><span className="atf-field"><small>a</small><span className="atf-input atf-static">{result.year}-12-31</span></span></div></section>
        <section className="atf-quadro"><header><span className="atf-qn">02</span><span>ÁREA DA SEDE, DIREÇÃO EFETIVA OU ESTABELECIMENTO ESTÁVEL</span></header>
          <div className="atf-row"><small className="atf-hint">Serviço de Finanças da sede: preenchido pela AT no Portal.</small></div></section>
      </div>
      <div className="atf-tax"><strong>IRC</strong><span>MODELO 22</span></div>
    </div>

    <section className="atf-quadro">
      <header><span className="atf-qn">03</span><span>IDENTIFICAÇÃO E CARACTERIZAÇÃO DO SUJEITO PASSIVO</span></header>
      <div className="atf-row">
        <span className="atf-field atf-grow"><small>1 — Designação</small><span className="atf-input atf-static">{companyName}</span></span>
        <span className="atf-field"><small>2 — N.º de Identificação Fiscal (NIF)</small><span className="atf-input atf-static">{taxNumber}</span></span>
      </div>
      <div className="atf-sub">3 — TIPO DE SUJEITO PASSIVO</div>
      <div className="atf-row atf-checks-grid">{TIPOS_SUJEITO.map((label, index) => <Check key={label} code="tipo-sujeito-passivo" value={index + 1} label={label} />)}</div>
    </section>

    <section className="atf-quadro">
      <header><span className="atf-qn">04</span><span>CARACTERÍSTICAS DA DECLARAÇÃO</span></header>
      <div className="atf-sub">1 — TIPO DE DECLARAÇÃO</div>
      <div className="atf-row atf-checks-grid">{TIPOS_DECLARACAO.map((label, index) => <Check key={label} code="tipo-declaracao" value={index + 1} label={label} />)}</div>
    </section>

    <section className="atf-quadro">
      <header><span className="atf-qn">05</span><span>IDENTIFICAÇÃO DO REPRESENTANTE LEGAL E DO CONTABILISTA CERTIFICADO</span></header>
      <div className="atf-row"><Nif code="nif-representante" label="1 — NIF do representante legal" /><Nif code="nif-cc" label="2 — NIF do contabilista certificado" /></div>
    </section>

    <section className="atf-quadro">
      <header><span className="atf-qn">07</span><span>APURAMENTO DO LUCRO TRIBUTÁVEL</span></header>
      <table className="atf-table atf-q07">
        <tbody>
          <Row code="701" label="RESULTADO LÍQUIDO DO PERÍODO" />
          {VARIACOES.map((number) => <Row key={number} code={String(number)} />)}
          <Row code="708" label="SOMA (campos 701 + 702 + 703 − 704 − 705 + 706 − 707 + 805 − 806)" />
          <tr className="atf-band"><td colSpan={2}>A ACRESCER</td></tr>
          {ACRESCER.map((number) => <Row key={number} code={String(number)} />)}
          <Row code="753" label="SOMA (campos 708 a 752)" />
          <tr className="atf-band"><td colSpan={2}>A DEDUZIR</td></tr>
          {DEDUZIR.map((number) => <Row key={number} code={String(number)} />)}
          <Row code="776" label="SOMA (campos 754 a 775)" />
          <Row code="777" label="PREJUÍZO PARA EFEITOS FISCAIS (se 776 > 753)" />
          <Row code="778" label="LUCRO TRIBUTÁVEL (se 753 ≥ 776) (a transportar para o quadro 09)" />
        </tbody>
      </table>
    </section>

    <section className="atf-quadro">
      <header><span className="atf-qn">08</span><span>REGIMES DE TAXA</span></header>
      <div className="atf-sub">08.1 — REGIMES DE REDUÇÃO DE TAXA <small>(assinalar com X · taxas de tributação)</small></div>
      <div className="atf-regimes">{REDUCAO_TAXA.map(([number, label, rate]) => <label key={number} className="atf-regime"><span>{label}</span><span className="atf-num atf-num-box">{number}</span><input type="checkbox" checked={pick('regime-reducao-taxa') === number} onChange={() => setPick('regime-reducao-taxa', number)} aria-label={`Campo ${number}`} /><em>{rate}</em></label>)}</div>
      <div className="atf-sub">08.2 — REGIME GERAL</div>
      <div className="atf-regimes">{REGIME_GERAL.map(([number, label, rate]) => <label key={number} className="atf-regime"><span>{label}</span><span className="atf-num atf-num-box">{number}</span><input type="checkbox" checked={pick('regime-geral') === number} onChange={() => setPick('regime-geral', number)} aria-label={`Campo ${number}`} /><em>{rate}</em></label>)}</div>
    </section>

    <section className="atf-quadro">
      <header><span className="atf-qn">09</span><span>APURAMENTO DA MATÉRIA COLETÁVEL</span></header>
      <table className="atf-table atf-q09">
        <thead><tr><th>(transporte do Q. 07)</th><th>Regime geral</th><th>Com redução de taxa</th><th>Com isenção</th><th>Regime simplificado</th></tr></thead>
        <tbody>
          <tr><td>1. PREJUÍZO FISCAL</td><td><F code="301" total /></td><td><F code="312" /></td><td><F code="323" /></td><td /></tr>
          <tr><td>2. LUCRO TRIBUTÁVEL</td><td><F code="302" total /></td><td><F code="313" /></td><td><F code="324" /></td><td><F code="400" /></td></tr>
          <tr><td>Prejuízos fiscais dedutíveis</td><td><F code="303" /></td><td><F code="314" /></td><td><F code="325" /></td><td><F code="401" /></td></tr>
          <tr><td>3. DEDUÇÕES: Prejuízos fiscais deduzidos</td><td><F code="309" /></td><td><F code="320" /></td><td><F code="331" /></td><td><F code="407" /></td></tr>
          <tr><td>Benefícios fiscais</td><td><F code="310" /></td><td><F code="321" /></td><td><F code="332" /></td><td><F code="408" /></td></tr>
          <tr><td>4. MATÉRIA COLETÁVEL (2 − 3)</td><td><F code="311" total /></td><td><F code="322" /></td><td><F code="333" /></td><td><F code="409" /></td></tr>
        </tbody>
      </table>
      <p className="atf-hint atf-pad">Só a coluna do regime geral é apurada pela aplicação; as outras colunas ficam para preencher no Portal. Prejuízos dedutíveis (303): {amount.format(byCode.get('prejuizos-disponiveis')?.amount ?? 0)} — a reportar: {amount.format(byCode.get('prejuizos-a-reportar')?.amount ?? 0)}.</p>
    </section>

    <section className="atf-quadro">
      <header><span className="atf-qn">10</span><span>CÁLCULO DO IMPOSTO</span></header>
      <div className="atf-row atf-q10-options">
        <label className="atf-check"><input type="checkbox" checked={sme} onChange={(event) => onSme(event.target.checked)} aria-label="PME ou Small Mid Cap — taxa reduzida na primeira parcela" /><span>PME / Small Mid Cap: taxa reduzida na primeira parcela (campo 347-A)</span></label>
        <span className="atf-field"><small>Taxa da derrama municipal (%)</small><input className="atf-input atf-rate" aria-label="Taxa da derrama municipal" inputMode="decimal" value={municipalRate} onChange={(event) => onMunicipalRate(event.target.value)} placeholder="ex.: 1,5" /></span>
      </div>
      <table className="atf-table atf-q10">
        <tbody>
          <tr><td>Imposto à taxa normal (art.º 87.º, n.ºs 2 e 8) — PME / Small Mid Cap, primeira parcela</td><td><F code="347-A" /></td><td /></tr>
          <tr><td>Imposto à taxa normal (art.º 87.º, n.º 1)</td><td><F code="347-B" /></td><td /></tr>
          <tr><td>Imposto a outras taxas <span className="atf-inline"><F code="348" /> %</span></td><td><F code="349" /></td><td /></tr>
          <tr><td>Imposto imputável à Região Autónoma dos Açores</td><td><F code="350" /></td><td /></tr>
          <tr><td>Imposto imputável à Região Autónoma da Madeira</td><td><F code="370" /></td><td /></tr>
          <tr className="atf-computed"><td>COLETA (347-A + 347-B + 349 + 350 + 370)</td><td /><td><F code="351" total /></td></tr>
          <tr><td>Derrama estadual (art.º 87.º-A)</td><td><F code="373" /></td><td /></tr>
          <tr className="atf-computed"><td>COLETA TOTAL (351 + 373)</td><td /><td><F code="378" total /></td></tr>
          <tr><td>Dupla tributação jurídica internacional (DTJI — art.º 91.º)</td><td><F code="353" /></td><td /></tr>
          <tr><td>Dupla tributação económica internacional (art.º 91.º-A)</td><td><F code="375" /></td><td /></tr>
          <tr><td>Benefícios fiscais</td><td><F code="355" /></td><td /></tr>
          <tr><td>Adicional ao Imposto Municipal sobre Imóveis (art.º 135.º-J do CIMI)</td><td><F code="470" /></td><td /></tr>
          <tr><td>Pagamento especial por conta (art.º 93.º)</td><td><F code="356" /></td><td /></tr>
          <tr className="atf-computed"><td>TOTAL DAS DEDUÇÕES (353 + 375 + 355 + 356 + 470) ≤ 378</td><td /><td><F code="357" total /></td></tr>
          <tr className="atf-computed"><td>TOTAL DO IRC LIQUIDADO (378 − 357) ≥ 0</td><td /><td><F code="358" total /></td></tr>
          <tr><td>Resultado da liquidação (art.º 92.º)</td><td /><td><F code="371" /></td></tr>
          <tr><td>Retenções na fonte</td><td><F code="359" /></td><td /></tr>
          <tr><td>Pagamentos por conta (art.º 105.º)</td><td><F code="360" /></td><td /></tr>
          <tr><td>Pagamentos adicionais por conta (art.º 105.º-A)</td><td><F code="374" /></td><td /></tr>
          <tr className="atf-computed"><td>IRC A PAGAR (358 + 371 − 359 − 360 − 374) &gt; 0</td><td /><td><F code="361" total /></td></tr>
          <tr className="atf-computed"><td>IRC A RECUPERAR (358 + 371 − 359 − 360 − 374) &lt; 0</td><td /><td><F code="362" total /></td></tr>
          <tr><td>IRC de períodos anteriores</td><td><F code="363" /></td><td /></tr>
          <tr><td>Reposição de benefícios fiscais</td><td><F code="372" /></td><td /></tr>
          <tr><td>Derrama municipal</td><td><F code="364" /></td><td /></tr>
          <tr><td>Dupla tributação jurídica internacional (art.º 91.º) — países com CDT e quando DTJI &gt; 378</td><td><F code="379" /></td><td /></tr>
          <tr><td>Tributações autónomas <small className="atf-hint">(ou o total dos encargos indicados na vista «Tabela»)</small></td><td><F code="365" /></td><td /></tr>
          <tr><td>Juros compensatórios</td><td><F code="366" /></td><td /></tr>
          <tr><td>Juros de mora</td><td><F code="369" /></td><td /></tr>
          <tr className="atf-computed"><td>TOTAL A PAGAR [361 ou (−362) + 363 + 372 + 364 − 379 + 365 + 366 + 369] &gt; 0</td><td /><td><F code="367" total /></td></tr>
          <tr className="atf-computed"><td>TOTAL A RECUPERAR [(−362) + 363 + 372 + 364 − 379 + 365 + 366 + 369] &lt; 0</td><td /><td><F code="368" total /></td></tr>
        </tbody>
      </table>
    </section>

    <section className="atf-quadro">
      <header><span className="atf-qn">11</span><span>OUTRAS INFORMAÇÕES</span></header>
      <div className="atf-row atf-q11">
        <span className="atf-field"><small>Total de rendimentos do período</small><F code="410" total /></span>
        <span className="atf-field"><small>Volume de negócios do período</small><F code="411" total /></span>
      </div>
    </section>
    <p className="atf-hint atf-pad">Os quadros 06, 10-A, 10-B, 11-A, 11-B, 12, 13 e 14 e os anexos não são preenchidos pela aplicação.</p>
  </div>
}
