import { fireEvent, render, screen } from '@testing-library/react'
import type { Modelo22Field, Modelo22Return } from '../app/demo-data'
import { Modelo22Form } from './Modelo22Form'

const field = (code: string, section: string, amount: number, computed: boolean, extra: Partial<Modelo22Field> = {}): Modelo22Field =>
  ({ code, section, label: `Campo ${code}`, amount, computed, note: null, suggested: null, suggestionSource: null, ...extra })

const result: Modelo22Return = {
  year: 2026, smallOrMediumEnterprise: true, municipalSurtaxRate: 1.5, warnings: [], source: '', disclaimer: '',
  fields: [
    field('701', 'Quadro 07', 12345.67, true),
    field('702', 'Quadro 07', 0, false),
    field('724', 'Quadro 07', 0, false, { suggested: 4500, suggestionSource: 'saldo da conta 8121' }),
    field('708', 'Quadro 07', 12345.67, true),
    field('753', 'Quadro 07', 12345.67, true),
    field('776', 'Quadro 07', 0, true),
    field('777', 'Quadro 07', 0, true),
    field('778', 'Quadro 07', 12345.67, true),
    field('302', 'Quadro 09', 12345.67, true),
    field('prejuizos-disponiveis', 'Quadro 09', 800, true),
    field('309', 'Quadro 09', 0, false),
    field('311', 'Quadro 09', 12345.67, true),
    field('347-A', 'Quadro 10', 2000, true),
    field('351', 'Quadro 10', 2000, true),
    field('367', 'Quadro 10', 2000, true),
    field('410', 'Quadro 11', 50000, true),
    field('tipo-sujeito-passivo', 'Rosto', 1, false),
  ],
}

describe('impresso do Modelo 22', () => {
  it('mostra os campos calculados, edita os campos à mão e os assinalamentos', () => {
    const onDraft = vi.fn()
    render(<Modelo22Form result={result} draft={{ 'tipo-sujeito-passivo': '1' }} onDraft={onDraft} companyName="Empresa, Lda." taxNumber="500000000" sme onSme={() => {}} municipalRate="1,5" onMunicipalRate={() => {}} />)

    expect(screen.getByText('500000000')).toBeInTheDocument()
    expect(screen.getAllByText(/12[\s ]345,67/).length).toBeGreaterThanOrEqual(4)
    expect(screen.getByLabelText('Residente que exerce, a título principal, atividade comercial, industrial ou agrícola')).toBeChecked()
    expect(screen.getByText('800,00')).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Campo 702'), { target: { value: '100' } })
    expect(onDraft).toHaveBeenLastCalledWith('702', '100')
    fireEvent.click(screen.getByRole('button', { name: /usar 4[\s ]500,00/ }))
    expect(onDraft).toHaveBeenLastCalledWith('724', '4500')
    fireEvent.click(screen.getByLabelText('Declaração de substituição (art.º 122.º, n.ºs 1 e 2)'))
    expect(onDraft).toHaveBeenLastCalledWith('tipo-declaracao', '2')
    // Voltar a carregar no assinalado tira-o.
    fireEvent.click(screen.getByLabelText('Residente que exerce, a título principal, atividade comercial, industrial ou agrícola'))
    expect(onDraft).toHaveBeenLastCalledWith('tipo-sujeito-passivo', '')
    fireEvent.click(screen.getByLabelText('Campo 246'))
    expect(onDraft).toHaveBeenLastCalledWith('regime-geral', '246')
    fireEvent.change(screen.getByLabelText('2 — NIF do contabilista certificado'), { target: { value: '12a3' } })
    expect(onDraft).toHaveBeenLastCalledWith('nif-cc', '123')
  })
})
