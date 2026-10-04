import { CircleAlert, CircleCheck } from 'lucide-react'
import { Link } from 'react-router-dom'
import { euro, type SuggestedEntryLine, type SuggestedEntryOutcome } from '../app/demo-data'

// Peças comuns às sugestões das compras (e-Fatura) e das vendas (SAF-T de faturação).

export function SuggestionLines({ lines }: { lines: SuggestedEntryLine[] }) {
  return <table className="nested-lines">
    <tbody>{lines.map((line, index) => <tr key={`${line.accountCode}-${index}`}>
      <td><Link to={`/contas/${line.accountCode}`}>{line.accountCode}</Link> {line.accountName}{line.vatCode ? <small> · {line.vatCode}</small> : null}{line.partyTaxNumber ? <small> · NIF {line.partyTaxNumber}</small> : null}</td>
      <td className="numeric">{line.debit ? euro.format(line.debit) : ''}</td>
      <td className="numeric">{line.credit ? euro.format(line.credit) : ''}</td>
    </tr>)}</tbody>
  </table>
}

export function SuggestionOutcomes({ outcomes }: { outcomes: SuggestedEntryOutcome[] }) {
  const created = outcomes.filter((outcome) => outcome.reference)
  const failed = outcomes.filter((outcome) => outcome.error)
  return <>
    {created.length > 0 && <p className="save-status"><CircleCheck size={15} /> {created.length === 1 ? 'Criado 1 lançamento por conferir' : `Criados ${created.length} lançamentos por conferir`}: {created.map((outcome, index) => <span key={outcome.reference}>{index > 0 ? ', ' : ''}<Link to={`/movimentos/${outcome.reference}`}>{outcome.reference}</Link></span>)}.</p>}
    {failed.length > 0 && <p className="save-status error"><CircleAlert size={15} /> {failed.length === 1 ? '1 sugestão não foi lançada' : `${failed.length} sugestões não foram lançadas`}: {failed.map((outcome) => outcome.error).filter((value, index, all) => all.indexOf(value) === index).join(' ')}</p>}
  </>
}

export const percentLabel = (value: number) => `${value.toLocaleString('pt-PT', { maximumFractionDigits: 2 })} %`
