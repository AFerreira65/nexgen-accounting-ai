import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { DocumentAttachmentRow } from '../app/demo-data'
import { EntryDocumentViewer } from './EntryDocumentViewer'

const row = (id: string, fileName: string, patch: Partial<DocumentAttachmentRow> = {}): DocumentAttachmentRow => ({
  id, fileName, contentType: 'application/pdf', sizeBytes: 1000, sha256: id, uploadedAtUtc: '2026-10-05T10:00:00Z', notes: null,
  entryReference: null, qrIssuerTaxId: null, qrDocumentNo: null, qrDocumentDate: null, qrGrossTotal: null, qrTaxTotal: null, canDelete: true, ...patch,
})

const { attachments } = vi.hoisted(() => ({ attachments: vi.fn() }))
vi.mock('../app/WorkContext', () => ({
  useWorkContext: () => ({
    activeCompany: { companyId: 'c1' },
    attachments,
    uploadAttachment: vi.fn(),
    attachmentUrl: (_company: string, id: string) => `/content/${id}`,
  }),
}))

describe('documento ao lado das linhas', () => {
  beforeEach(() => attachments.mockReset())

  it('ao corrigir, mostra logo o documento já ligado ao lançamento', async () => {
    const own = row('a1', 'fatura.pdf', { entryReference: 'COMP-2026-00001', qrDocumentNo: 'FT A/1', qrGrossTotal: 123, qrIssuerTaxId: '500584095' })
    attachments.mockImplementation((_company: string, filter?: { entryReference?: string }) => Promise.resolve(filter?.entryReference ? [own] : [row('b1', 'solto.pdf')]))
    const onChoose = vi.fn()
    render(<EntryDocumentViewer reference="COMP-2026-00001" chosen={null} onChoose={onChoose} onClose={() => {}} />)
    await waitFor(() => expect(onChoose).toHaveBeenCalledWith(own))
    expect(attachments).toHaveBeenCalledWith('c1', { entryReference: 'COMP-2026-00001' })
    expect(attachments).toHaveBeenCalledWith('c1', { unlinked: true, search: undefined })
  })

  it('num lançamento novo, oferece os documentos sem lançamento e mostra o escolhido com o código QR', async () => {
    const free = row('b1', 'solto.pdf', { qrDocumentNo: 'FT B/7', qrDocumentDate: '2026-09-30', qrGrossTotal: 61.5, qrTaxTotal: 11.5 })
    attachments.mockResolvedValue([free])
    const onChoose = vi.fn()
    const { rerender } = render(<EntryDocumentViewer reference={null} chosen={null} onChoose={onChoose} onClose={() => {}} />)
    await screen.findByRole('option', { name: /solto\.pdf · FT B\/7 · sem lançamento/ })
    expect(onChoose).not.toHaveBeenCalled()
    fireEvent.change(screen.getByLabelText('Documento a ver'), { target: { value: 'b1' } })
    expect(onChoose).toHaveBeenCalledWith(free)

    rerender(<EntryDocumentViewer reference={null} chosen={free} onChoose={onChoose} onClose={() => {}} />)
    expect(screen.getByTitle('solto.pdf')).toHaveAttribute('src', '/content/b1')
    expect(screen.getByText(/Código QR:/)).toHaveTextContent('FT B/7 de 2026-09-30')
  })

  it('um TIFF abre noutra janela, porque o navegador não o mostra', () => {
    attachments.mockResolvedValue([])
    render(<EntryDocumentViewer reference={null} chosen={row('t1', 'digitalizada.tif', { contentType: 'image/tiff' })} onChoose={() => {}} onClose={() => {}} />)
    expect(screen.getByText(/não mostra ficheiros TIFF/)).toBeInTheDocument()
  })
})
