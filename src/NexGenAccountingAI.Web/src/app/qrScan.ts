// Leitura do código QR da fatura no próprio browser, numa imagem ou num PDF.
// Nas imagens tenta primeiro a BarcodeDetector API (onde existe) e depois o jsQR;
// nos PDF desenha as primeiras páginas com o pdf.js e procura o código no
// resultado, por isso serve tanto para o código desenhado na página como para
// o que vem como imagem. Nada sai do browser.

import jsQR from 'jsqr'

type DetectedBarcode = { rawValue: string; format: string }
type BarcodeDetectorLike = { detect: (source: ImageBitmapSource) => Promise<DetectedBarcode[]> }
type BarcodeDetectorConstructor = new (options?: { formats?: string[] }) => BarcodeDetectorLike

/** As páginas de um PDF onde se procura o código: a especificação põe-no na fatura, normalmente na primeira. */
export const pdfPagesScanned = 3
/** Escala a que a página é desenhada: com 3, um código de 30 mm fica com cerca de 255 pixels. */
const pdfScale = 3

function detectorConstructor(): BarcodeDetectorConstructor | null {
  const candidate = (globalThis as { BarcodeDetector?: BarcodeDetectorConstructor }).BarcodeDetector
  return typeof candidate === 'function' ? candidate : null
}

const isInvoiceQr = (value: string) => value.startsWith('A:')

/** O texto do código QR de fatura nestes pixels (RGBA), ou null. */
export function findInvoiceQr(pixels: Uint8ClampedArray, width: number, height: number): string | null {
  const code = jsQR(pixels, width, height, { inversionAttempts: 'dontInvert' })
  const text = code?.data.trim()
  return text && isInvoiceQr(text) ? text : null
}

export const isPdf = (file: File) => file.type === 'application/pdf' || file.name.toLowerCase().endsWith('.pdf')

export const canScanQrInBrowser = () => typeof document !== 'undefined' && typeof createImageBitmap === 'function'

async function readFromImage(file: File): Promise<string | null> {
  const bitmap = await createImageBitmap(file)
  try {
    const Detector = detectorConstructor()
    if (Detector) {
      const codes = await new Detector({ formats: ['qr_code'] }).detect(bitmap).catch(() => [])
      const found = codes.map((code) => code.rawValue.trim()).find(isInvoiceQr)
      if (found) return found
    }
    const canvas = document.createElement('canvas')
    canvas.width = bitmap.width
    canvas.height = bitmap.height
    const context = canvas.getContext('2d', { willReadFrequently: true })
    if (!context) return null
    context.drawImage(bitmap, 0, 0)
    return findInvoiceQr(context.getImageData(0, 0, canvas.width, canvas.height).data, canvas.width, canvas.height)
  } finally {
    bitmap.close()
  }
}

type PdfJs = typeof import('pdfjs-dist')
type PageCanvas = { canvas: unknown; context: { getImageData: (x: number, y: number, w: number, h: number) => { data: Uint8ClampedArray } } }

/**
 * Procura o código nas primeiras páginas do PDF. O pdf.js e a forma de criar a
 * superfície de desenho vêm de fora, para o mesmo código correr no browser e nos testes.
 */
export async function scanPdfForInvoiceQr(pdfjs: PdfJs, data: Uint8Array, newCanvas: (width: number, height: number) => PageCanvas): Promise<string | null> {
  const loading = pdfjs.getDocument({ data })
  try {
    const pdf = await loading.promise
    for (let number = 1; number <= Math.min(pdf.numPages, pdfPagesScanned); number++) {
      const page = await pdf.getPage(number)
      const viewport = page.getViewport({ scale: pdfScale })
      const width = Math.ceil(viewport.width)
      const height = Math.ceil(viewport.height)
      const surface = newCanvas(width, height)
      await page.render({ canvas: null, canvasContext: surface.context as unknown as CanvasRenderingContext2D, viewport, background: 'white' }).promise
      const found = findInvoiceQr(surface.context.getImageData(0, 0, width, height).data, width, height)
      page.cleanup()
      if (found) return found
    }
    return null
  } finally {
    await loading.destroy()
  }
}

async function readFromPdf(file: File): Promise<string | null> {
  // O pdf.js só é carregado quando aparece um PDF.
  const [pdfjs, worker] = await Promise.all([import('pdfjs-dist'), import('pdfjs-dist/build/pdf.worker.min.mjs?url')])
  pdfjs.GlobalWorkerOptions.workerSrc = worker.default
  return scanPdfForInvoiceQr(pdfjs, new Uint8Array(await file.arrayBuffer()), (width, height) => {
    const canvas = document.createElement('canvas')
    canvas.width = width
    canvas.height = height
    const context = canvas.getContext('2d', { willReadFrequently: true })
    if (!context) throw new Error('Sem contexto de desenho.')
    return { canvas, context }
  })
}

/** O texto do primeiro código QR de fatura (começa por «A:») encontrado na imagem ou no PDF, ou null. */
export async function readInvoiceQr(file: File): Promise<string | null> {
  if (!canScanQrInBrowser()) return null
  try {
    if (file.type.startsWith('image/')) return await readFromImage(file)
    if (isPdf(file)) return await readFromPdf(file)
    return null
  } catch {
    return null
  }
}
