import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { AiUsagePanel } from './AiUsagePanel'

const json = (body: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }))
const status = (spent: number, allowance: number | null, extra = 0) => ({ allowanceUsd: allowance, allowanceIsDefault: false, spentThisMonthUsd: spent, extraCreditUsd: extra, availableUsd: allowance === null ? null : Math.max(0, allowance - spent) + extra, callsThisMonth: 3, unlimited: allowance === null, exhausted: allowance !== null && allowance - spent + extra <= 0 })
const config = { config: { defaultMonthlyAllowanceUsd: null, chatModel: null, reportModel: null, quickModel: null, pricesJson: null }, models: ['claude-haiku-4-5-20251001', 'claude-sonnet-5-5', 'claude-opus-5-5'], defaults: { chat: 'claude-haiku-4-5-20251001', report: 'claude-sonnet-5-5', quick: 'claude-haiku-4-5-20251001' }, prices: { 'claude-haiku-4-5-20251001': { input: 1, output: 5 }, 'claude-sonnet-5-5': { input: 3, output: 15 }, 'claude-opus-5-5': { input: 5, output: 25 } } }

describe('consumo e limites da IA', () => {
  const fetchMock = vi.fn()
  beforeEach(() => { fetchMock.mockReset(); vi.stubGlobal('fetch', fetchMock) })
  afterEach(() => vi.unstubAllGlobals())

  it('mostra o consumo por empresa, o esgotado, e grava a franquia e o crédito', async () => {
    fetchMock.mockImplementation((url: string) => {
      if (url.endsWith('/usage-config')) return json(config)
      if (url.endsWith('/budgets')) return json([
        { companyId: 'c1', companyName: 'Empresa A, Lda.', firmName: 'Gabinete', status: status(0.5, 2) },
        { companyId: 'c2', companyName: 'Empresa B, Lda.', firmName: 'Gabinete', status: status(2.5, 2) },
      ])
      return json({})
    })
    render(<AiUsagePanel />)
    expect(await screen.findByText('Empresa A, Lda.')).toBeInTheDocument()
    expect(screen.getByText('esgotado')).toBeInTheDocument()
    expect(screen.getAllByText(/o mais barato/, { selector: 'option' }).length).toBeGreaterThan(0)

    fireEvent.change(screen.getByLabelText('Franquia mensal de Empresa A, Lda.'), { target: { value: '5' } })
    fireEvent.click(screen.getAllByRole('button', { name: 'Guardar' })[1])
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/v1/producer/ai-settings/budgets/c1', expect.objectContaining({ method: 'PUT' })))
    expect(JSON.parse((fetchMock.mock.calls.find((call) => call[0] === '/api/v1/producer/ai-settings/budgets/c1')![1] as RequestInit).body as string)).toEqual({ monthlyAllowanceUsd: 5 })

    fireEvent.change(screen.getByLabelText('Crédito a juntar a Empresa B, Lda.'), { target: { value: '3,5' } })
    fireEvent.click(screen.getAllByRole('button', { name: /Juntar/ })[1])
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/v1/producer/ai-settings/budgets/c2/credit', expect.objectContaining({ method: 'POST' })))
    expect(JSON.parse((fetchMock.mock.calls.find((call) => call[0] === '/api/v1/producer/ai-settings/budgets/c2/credit')![1] as RequestInit).body as string)).toEqual({ amountUsd: 3.5, note: null })
  })
})
