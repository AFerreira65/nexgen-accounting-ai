import { useId } from 'react'

export type ChartTone = 'primary' | 'danger' | 'accent' | 'muted' | 'warning'

/** Uma série do gráfico. `null` num ponto interrompe a linha (ou omite a barra). */
export type ChartSeries = {
  name: string
  kind: 'bar' | 'line' | 'area'
  tone: ChartTone
  values: (number | null)[]
  dashed?: boolean
}

const compact = new Intl.NumberFormat('pt-PT', { notation: 'compact', maximumFractionDigits: 1 })

/** Passo «redondo» (1, 2 ou 5 × 10ⁿ) para cerca de cinco divisões do eixo. */
function niceStep(range: number) {
  const rough = range / 5
  const power = 10 ** Math.floor(Math.log10(rough))
  const fraction = rough / power
  return (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * power
}

export function chartScale(values: number[]) {
  const low = Math.min(0, ...values)
  const high = Math.max(0, ...values)
  if (low === high) return { min: 0, max: 1, ticks: [0, 1] }
  const step = niceStep(high - low)
  const min = Math.floor(low / step) * step
  const max = Math.ceil(high / step) * step
  const ticks: number[] = []
  for (let tick = min; tick <= max + step / 2; tick += step) ticks.push(Math.round(tick / step) * step)
  return { min, max, ticks }
}

/**
 * Gráfico de barras e linhas em SVG, sem dependências. Cada ponto tem um
 * `<title>` com o valor (aparece ao passar o rato e é lido pelos leitores de
 * ecrã); a legenda identifica as séries.
 */
export function Chart({ title, labels, series, format, height = 260 }: {
  title: string
  labels: string[]
  series: ChartSeries[]
  format: (value: number) => string
  height?: number
}) {
  const titleId = useId()
  const width = 720
  const margin = { top: 14, right: 12, bottom: 28, left: 56 }
  const plotWidth = width - margin.left - margin.right
  const plotHeight = height - margin.top - margin.bottom
  const { min, max, ticks } = chartScale(series.flatMap((item) => item.values.filter((value): value is number => value !== null)))
  const y = (value: number) => margin.top + plotHeight * (1 - (value - min) / (max - min))
  const band = plotWidth / Math.max(1, labels.length)
  const center = (index: number) => margin.left + band * (index + 0.5)
  const bars = series.filter((item) => item.kind === 'bar')
  const barWidth = Math.min(22, (band * 0.72) / Math.max(1, bars.length))
  const tip = (item: ChartSeries, index: number, value: number) => `${item.name} — ${labels[index]}: ${format(value)}`

  // Uma linha parte-se em troços onde há valores em falta.
  const segments = (values: (number | null)[]) => {
    const result: { index: number; value: number }[][] = []
    let run: { index: number; value: number }[] = []
    values.forEach((value, index) => {
      if (value === null) { if (run.length > 0) result.push(run); run = [] }
      else run.push({ index, value })
    })
    if (run.length > 0) result.push(run)
    return result
  }

  return <figure className="chart">
    <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-labelledby={titleId} preserveAspectRatio="xMidYMid meet">
      <title id={titleId}>{title}</title>
      {ticks.map((tick) => <g key={tick}>
        <line className={tick === 0 ? 'chart-axis' : 'chart-grid'} x1={margin.left} x2={width - margin.right} y1={y(tick)} y2={y(tick)} />
        <text className="chart-tick" x={margin.left - 8} y={y(tick)} textAnchor="end" dominantBaseline="middle">{compact.format(tick)}</text>
      </g>)}
      {labels.map((label, index) => <text key={label} className="chart-tick" x={center(index)} y={height - 8} textAnchor="middle">{label}</text>)}

      {bars.map((item, barIndex) => item.values.map((value, index) => value === null ? null : <rect
        key={`${item.name}-${index}`}
        className={`chart-bar tone-${item.tone}`}
        x={center(index) - (bars.length * barWidth) / 2 + barIndex * barWidth + 1}
        width={Math.max(1, barWidth - 2)}
        y={Math.min(y(value), y(0))}
        height={Math.max(1, Math.abs(y(value) - y(0)))}
        rx={2}
      ><title>{tip(item, index, value)}</title></rect>))}

      {series.filter((item) => item.kind !== 'bar').map((item) => <g key={item.name} className={`tone-${item.tone}`}>
        {segments(item.values).map((run) => {
          const points = run.map((point) => `${center(point.index)},${y(point.value)}`).join(' ')
          return <g key={run[0].index}>
            {item.kind === 'area' && <polygon className="chart-area" points={`${center(run[0].index)},${y(0)} ${points} ${center(run[run.length - 1].index)},${y(0)}`} />}
            <polyline className={`chart-line${item.dashed ? ' dashed' : ''}`} points={points} />
          </g>
        })}
        {item.values.map((value, index) => value === null ? null : <circle key={index} className="chart-point" cx={center(index)} cy={y(value)} r={3}>
          <title>{tip(item, index, value)}</title>
        </circle>)}
      </g>)}
    </svg>
    <figcaption className="chart-legend">
      {series.map((item) => <span key={item.name} className={`tone-${item.tone}`}><i className={item.kind === 'bar' ? 'swatch-bar' : `swatch-line${item.dashed ? ' dashed' : ''}`} />{item.name}</span>)}
    </figcaption>
  </figure>
}
