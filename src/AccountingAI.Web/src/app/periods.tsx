// Atalhos de período para os relatórios (balancete, DR, balanço, livro diário e
// razão). As datas são ISO (AAAA-MM-DD), sempre dentro do exercício escolhido.

const iso = (date: Date) => date.toISOString().slice(0, 10)

/** Data de referência: hoje, mas nunca depois do fim do exercício. */
export function periodReference(fiscalYear: number, today = new Date()): Date {
  const endOfFiscalYear = new Date(Date.UTC(fiscalYear, 11, 31))
  return today.getTime() < endOfFiscalYear.getTime() ? today : endOfFiscalYear
}

export function monthRange(reference: Date): { from: string; to: string } {
  const year = reference.getUTCFullYear()
  const month = reference.getUTCMonth()
  return { from: iso(new Date(Date.UTC(year, month, 1))), to: iso(new Date(Date.UTC(year, month + 1, 0))) }
}

export function quarterRange(reference: Date): { from: string; to: string } {
  const year = reference.getUTCFullYear()
  const firstMonth = Math.floor(reference.getUTCMonth() / 3) * 3
  return { from: iso(new Date(Date.UTC(year, firstMonth, 1))), to: iso(new Date(Date.UTC(year, firstMonth + 3, 0))) }
}

export function yearRange(fiscalYear: number): { from: string; to: string } {
  return { from: `${fiscalYear}-01-01`, to: `${fiscalYear}-12-31` }
}

export function PeriodShortcuts({ fiscalYear, onPick }: { fiscalYear: number; onPick: (from: string, to: string) => void }) {
  const reference = periodReference(fiscalYear)
  const item = (label: string, range: { from: string; to: string }) => (
    <button type="button" className="period-chip" onClick={() => onPick(range.from, range.to)}>{label}</button>
  )
  return <div className="period-shortcuts" role="group" aria-label="Períodos rápidos">
    {item('Mês', monthRange(reference))}
    {item('Trimestre', quarterRange(reference))}
    {item('Ano', yearRange(fiscalYear))}
  </div>
}
