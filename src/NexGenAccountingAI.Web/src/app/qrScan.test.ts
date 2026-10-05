// @vitest-environment node
import { readFileSync } from 'node:fs'
import { createCanvas, loadImage } from '@napi-rs/canvas'
import * as pdfjs from 'pdfjs-dist/legacy/build/pdf.mjs'
import { findInvoiceQr, scanPdfForInvoiceQr } from './qrScan'

// PDF gerados com o exemplo 1 das especificações da AT (versão 17 do código, 30 x 30 mm),
// numa página A4 com texto: o código desenhado em vetores, o mesmo como imagem e nenhum.
const fixture = (name: string) => new Uint8Array(readFileSync(new URL(`../test-fixtures/${name}`, import.meta.url)))
const surface = (width: number, height: number) => {
  const canvas = createCanvas(width, height)
  return { canvas, context: canvas.getContext('2d') }
}

describe('código QR da fatura num PDF', () => {
  it('lê o código desenhado na página, sem imagem', async () => {
    const text = await scanPdfForInvoiceQr(pdfjs as never, fixture('invoice-qr-vector.pdf'), surface as never)
    expect(text).toMatch(/^A:123456789\*B:999999990\*/)
    expect(text).toContain('*O:513600.58*')
  }, 30000)

  it('lê o código que vem como imagem', async () => {
    const text = await scanPdfForInvoiceQr(pdfjs as never, fixture('invoice-qr-image.pdf'), surface as never)
    expect(text).toContain('G:FT AB2019/0035')
  }, 30000)

  it('devolve null sem código', async () => {
    expect(await scanPdfForInvoiceQr(pdfjs as never, fixture('no-qr.pdf'), surface as never)).toBeNull()
  }, 30000)
})

describe('código QR da fatura numa imagem', () => {
  it('lê o código sem a BarcodeDetector API, que o Edge e o Chrome no Windows não têm', async () => {
    const image = await loadImage(fixture('invoice-qr.png'))
    const { context } = surface(image.width, image.height)
    context.drawImage(image, 0, 0)
    const pixels = context.getImageData(0, 0, image.width, image.height)
    expect(findInvoiceQr(pixels.data, image.width, image.height)).toContain('G:FS CDVF/12345')
  })
})
