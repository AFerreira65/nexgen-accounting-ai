import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { ReportPackPage } from './ReportPackPage'

vi.mock('../app/WorkContext', () => ({
  useWorkContext: () => ({
    can: () => true,
    activeCompany: { companyId: 'c1', legalName: 'Empresa', settings: { defaultFiscalYear: 2026 } },
    source: 'database',
    user: { id: 'u1' },
  }),
}))

const json = (body: unknown, status = 200, headers?: Record<string, string>) => new Response(JSON.stringify(body), { status, headers })
const preset = {
  key: 'banca', label: 'Relatório para a banca', description: '',
  request: {
    title: 'Relatório económico-financeiro', subtitle: 'Elementos', year: 2026, fromMonth: 1, toMonth: 12, includeClosing: false, preparer: null,
    sections: [
      { kind: 'texto', title: 'Apresentação', text: 'Substitua este texto.', mapId: null, templateKey: null, depth: null, columnSet: null, enabled: true },
      { kind: 'mapa', title: 'Balanço', text: null, mapId: null, templateKey: 'balanco', depth: null, columnSet: null, enabled: true },
      { kind: 'racios', title: 'Rácios', text: null, mapId: null, templateKey: null, depth: null, columnSet: null, enabled: true },
    ],
  },
}

function serve(calls: { url: string; body?: unknown }[], narration: unknown) {
  return vi.fn((url: string, init?: RequestInit) => {
    calls.push({ url, body: init?.body ? JSON.parse(String(init.body)) : undefined })
    if (url.includes('/reports/pack/presets')) return Promise.resolve(json([preset]))
    if (url.endsWith('/maps')) return Promise.resolve(json({ maps: [{ id: 'm1', name: 'O meu mapa' }], templates: [{ key: 'balanco', name: 'Balanço sintético' }] }))
    if (url.endsWith('/reports/pack/narrate')) return Promise.resolve(json(narration))
    if (url.includes('/reports/pack?format=json')) return Promise.resolve(json({ title: 'Relatório económico-financeiro', period: 'Exercício de 2026', sections: [{ title: 'Apresentação', paragraphs: ['Olá.'], table: null, notes: null }, { title: 'Balanço', paragraphs: [], table: { header: ['Rubrica', 'Exercício'], rows: [{ cells: ['Total do ativo', '1 000,00'], level: 0, bold: true, isTitle: false }], numeric: [false, true] }, notes: ['Nota.'] }] }))
    if (url.endsWith('/reports/pack')) return Promise.resolve(new Response(new Blob(['%PDF']), { status: 200, headers: { 'Content-Disposition': 'attachment; filename="relatorio-2026.pdf"' } }))
    return Promise.resolve(json({}, 404))
  })
}

describe('relatório para a banca', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('parte do modelo, pré-visualiza e gera o PDF', async () => {
    const calls: { url: string; body?: unknown }[] = []
    vi.stubGlobal('fetch', serve(calls, { paragraphs: [], unavailable: null, model: 'x' }))
    vi.stubGlobal('URL', { ...URL, createObjectURL: vi.fn(() => 'blob:x'), revokeObjectURL: vi.fn() })
    render(<ReportPackPage />)

    expect(await screen.findByDisplayValue('Relatório económico-financeiro')).toBeInTheDocument()
    expect(screen.getByLabelText('Mapa da secção 2')).toHaveValue('modelo:balanco')
    fireEvent.click(screen.getByLabelText('Incluir a secção Rácios'))
    fireEvent.change(screen.getByLabelText('Texto da secção 1'), { target: { value: 'A empresa vende vidro.\n\nSegundo parágrafo.' } })
    fireEvent.change(screen.getByLabelText('Mapa da secção 2'), { target: { value: 'mapa:m1' } })

    fireEvent.click(screen.getByRole('button', { name: 'Pré-visualizar' }))
    expect(await screen.findByText('Total do ativo')).toBeInTheDocument()
    const previewCall = calls.find((call) => call.url.includes('format=json'))!
    const sent = previewCall.body as { sections: { kind: string; enabled: boolean; mapId: string | null; text: string }[] }
    expect(sent.sections[2].enabled).toBe(false)
    expect(sent.sections[1].mapId).toBe('m1')
    expect(sent.sections[0].text).toContain('Segundo parágrafo.')

    fireEvent.click(screen.getByRole('button', { name: 'Gerar PDF' }))
    await waitFor(() => expect(calls.some((call) => call.url.endsWith('/reports/pack'))).toBe(true))
    expect(await screen.findByText('PDF gerado.')).toBeInTheDocument()
  })

  it('mete o texto da IA na secção e diz quando a IA não está disponível', async () => {
    const calls: { url: string; body?: unknown }[] = []
    vi.stubGlobal('fetch', serve(calls, { paragraphs: ['Primeiro.', 'Segundo.'], unavailable: null, model: 'claude-opus-5-5' }))
    render(<ReportPackPage />)
    expect(await screen.findByDisplayValue('Apresentação')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Indicações para a IA'), { target: { value: 'Realçar a liquidez' } })
    fireEvent.click(screen.getByRole('button', { name: 'Redigir com IA' }))
    const text = () => (screen.getByLabelText('Texto da secção 1') as HTMLTextAreaElement).value
    await waitFor(() => expect(text()).toContain('Primeiro.\n\nSegundo.'))
    expect((calls.find((call) => call.url.endsWith('/narrate'))!.body as { instructions: string }).instructions).toBe('Realçar a liquidez')
    expect(text()).toContain('[IA]')

    vi.unstubAllGlobals()
    vi.stubGlobal('fetch', serve([], { paragraphs: [], unavailable: 'Os relatórios de gestão com IA estão desligados para esta empresa.', model: '—' }))
    fireEvent.click(screen.getByRole('button', { name: 'Redigir com IA' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('desligados para esta empresa')
  })
})
