import { fireEvent, render, screen } from '@testing-library/react'
import type { VatDeclaration, VatFormField } from '../app/demo-data'
import { EMPTY_INPUTS, VatDeclarationForm } from './VatDeclarationForm'

const field = (number: number, column: VatFormField['column'], amount: number, extra: Partial<VatFormField> = {}): VatFormField =>
  ({ number, label: `Campo ${number}`, column, amount, computed: false, supported: true, sources: [], ...extra })

const declaration: VatDeclaration = {
  from: '2026-01-01', to: '2026-03-31', period: '03T', seat: 'Continente',
  fields: [
    field(3, 'base', 1000, { sources: [{ accountCode: '7111', accountName: 'Vendas', amount: 1000 }] }),
    field(4, 'estado', 230),
    field(18, 'base', 300, { supported: false }),
    field(90, 'base', 1300, { computed: true }),
    field(91, 'sujeito-passivo', 0, { computed: true }),
    field(92, 'estado', 230, { computed: true }),
    field(93, 'apuramento', 230, { computed: true }),
    field(94, 'apuramento', 0, { computed: true }),
    field(95, 'apuramento', 0, { supported: false }),
    field(96, 'apuramento', 0, { computed: true }),
    field(102, 'desenvolvimento', 0, { supported: false }),
    field(106, 'desenvolvimento', 0, { computed: true }),
  ],
  warnings: [], accountingPayable: 230, accountingCredit: 0, difference: 0, source: '', disclaimer: '',
}

describe('impresso da declaração periódica de IVA', () => {
  it('mostra os campos apurados, grava os campos à mão ao sair e assinala os quadros', () => {
    const onChange = vi.fn()
    const onInspect = vi.fn()
    render(<VatDeclarationForm declaration={declaration} inputs={{ ...EMPTY_INPUTS, fields: { 18: 300 } }} companyName="Empresa, Lda." taxNumber="500000000" onChange={onChange} onInspect={onInspect} />)

    expect(screen.getByText('500000000')).toBeInTheDocument()
    expect(screen.getByText('03T')).toBeInTheDocument()
    expect(screen.getAllByLabelText('CONTINENTE')[0]).toBeChecked()
    expect(screen.getAllByLabelText('CONTINENTE')[1]).not.toBeChecked()
    // O campo apurado abre as contas; o total não tem contas e não abre.
    fireEvent.click(screen.getByRole('button', { name: /1[\s\u00a0]000,00/ }))
    expect(onInspect).toHaveBeenCalledWith(expect.objectContaining({ number: 3 }))
    expect(screen.getByRole('button', { name: /1[\s\u00a0]300,00/ })).toBeDisabled()

    // Um campo à mão: escreve-se com vírgula e grava-se ao sair.
    const campo102 = screen.getByLabelText('Campo 102')
    fireEvent.change(campo102, { target: { value: '1 234,5' } })
    expect(onChange).not.toHaveBeenCalled()
    fireEvent.blur(campo102)
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ fields: { 18: 300, 102: 1234.5 } }))
    // Apagar o valor tira o campo.
    const campo18 = screen.getByLabelText('Campo 18')
    expect(campo18).toHaveValue('300,00')
    fireEvent.change(campo18, { target: { value: '' } })
    fireEvent.blur(campo18)
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ fields: {} }))

    fireEvent.click(screen.getByLabelText('Fora do prazo'))
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ withinDeadline: false }))
    fireEvent.click(screen.getByLabelText('SIM (preencha também o Quadro 06-A)'))
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ operationsAsAcquirer: true }))
    const nif = screen.getByLabelText('NIF do contabilista certificado')
    fireEvent.change(nif, { target: { value: '123456789' } })
    fireEvent.blur(nif)
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ certifiedAccountantTaxNumber: '123456789' }))
  })

  it('com inexistência de operações, os campos à mão ficam fechados', () => {
    render(<VatDeclarationForm declaration={declaration} inputs={{ ...EMPTY_INPUTS, noOperations: true }} companyName="Empresa" taxNumber="500000000" onChange={() => {}} />)
    expect(screen.getByLabelText('Campo 102')).toBeDisabled()
    expect(screen.getByLabelText('Campo 95')).toBeDisabled()
  })
})
