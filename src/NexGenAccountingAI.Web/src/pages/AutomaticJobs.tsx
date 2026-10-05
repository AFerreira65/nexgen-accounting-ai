import { Clock } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useWorkContext } from '../app/WorkContext'

type Job = { key: string; name: string; schedule: string }
type Run = { jobKey: string; companyId: string | null; companyName: string | null; periodKey: string; status: string; attempts: number; startedAtUtc: string; finishedAtUtc: string | null; message: string | null }
type Jobs = { enabled: boolean; jobs: Job[]; runs: Run[] }

const STATUS: Record<string, { label: string; badge: string }> = {
  Running: { label: 'A correr', badge: '' },
  Succeeded: { label: 'Concluída', badge: 'success' },
  Failed: { label: 'Falhou', badge: 'warning' },
  Skipped: { label: 'Nada para fazer', badge: '' },
}
const dateTime = new Intl.DateTimeFormat('pt-PT', { dateStyle: 'short', timeStyle: 'short' })

/** As tarefas que a aplicação corre sozinha (F4-05) e o que aconteceu nas últimas execuções. */
export function AutomaticJobs() {
  const { user } = useWorkContext()
  const [data, setData] = useState<Jobs | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    void fetch(`/api/v1/development/users/${user.id}/background-jobs`, { signal: controller.signal })
      .then(async (response) => (response.ok ? await response.json() as Jobs : null))
      .then((value) => { if (!controller.signal.aborted) setData(value) })
      .catch(() => { /* painel informativo */ })
    return () => controller.abort()
  }, [user.id])

  if (!data) return null
  const jobName = (key: string) => data.jobs.find((job) => job.key === key)?.name ?? key

  return <section className="panel">
    <div className="panel-heading"><div><h2><Clock size={17} /> Tarefas automáticas</h2><p>O que a aplicação faz sozinha, sem ninguém carregar em nada.{!data.enabled && ' Estão desligadas na configuração deste servidor.'}</p></div></div>
    <div className="table-wrap"><table>
      <thead><tr><th>Tarefa</th><th>Quando corre</th></tr></thead>
      <tbody>{data.jobs.map((job) => <tr key={job.key}><td className="primary-cell">{job.name}</td><td>{job.schedule}</td></tr>)}</tbody>
    </table></div>
    {data.runs.length === 0
      ? <p className="statement-note">Ainda não há execuções registadas.</p>
      : <div className="table-wrap"><table>
          <thead><tr><th>Início</th><th>Tarefa</th><th>Empresa</th><th>Período</th><th>Resultado</th><th>O que aconteceu</th></tr></thead>
          <tbody>{data.runs.map((run) => {
            const status = STATUS[run.status] ?? { label: run.status, badge: '' }
            return <tr key={`${run.jobKey}-${run.companyId ?? 'geral'}-${run.periodKey}`}>
              <td>{dateTime.format(new Date(run.startedAtUtc))}</td>
              <td>{jobName(run.jobKey)}</td>
              <td>{run.companyName ?? '—'}</td>
              <td>{run.periodKey}</td>
              <td><span className={`badge ${status.badge}`}>{status.label}</span>{run.attempts > 1 ? <small> {run.attempts}.ª tentativa</small> : null}</td>
              <td>{run.message ?? ''}</td>
            </tr>
          })}</tbody>
        </table></div>}
  </section>
}
