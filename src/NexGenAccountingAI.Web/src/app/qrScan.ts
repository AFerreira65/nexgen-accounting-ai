// Leitura do código QR de uma imagem no próprio browser, com a BarcodeDetector API
// quando existe (Chrome/Edge em vários sistemas). Sem ela — ou num PDF — o
// utilizador usa um leitor de códigos ou cola o texto.

type DetectedBarcode = { rawValue: string; format: string }
type BarcodeDetectorLike = { detect: (source: ImageBitmapSource) => Promise<DetectedBarcode[]> }
type BarcodeDetectorConstructor = new (options?: { formats?: string[] }) => BarcodeDetectorLike

function detectorConstructor(): BarcodeDetectorConstructor | null {
  const candidate = (globalThis as { BarcodeDetector?: BarcodeDetectorConstructor }).BarcodeDetector
  return typeof candidate === 'function' ? candidate : null
}

export const canScanQrInBrowser = () => detectorConstructor() !== null && typeof createImageBitmap === 'function'

/** O texto do primeiro código QR de fatura (começa por «A:») encontrado na imagem, ou null. */
export async function readInvoiceQr(file: File): Promise<string | null> {
  const Detector = detectorConstructor()
  if (!Detector || !file.type.startsWith('image/')) return null
  try {
    const bitmap = await createImageBitmap(file)
    const codes = await new Detector({ formats: ['qr_code'] }).detect(bitmap)
    bitmap.close()
    return codes.map((code) => code.rawValue.trim()).find((value) => value.startsWith('A:')) ?? null
  } catch {
    return null
  }
}
