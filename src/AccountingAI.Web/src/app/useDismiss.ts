import { useEffect, type RefObject } from 'react'

// Fecha um menu/popover ao clicar fora do seu contentor ou ao carregar em
// Escape. Só escuta enquanto `active` — o custo de subscrever é irrelevante
// porque um menu está aberto pouco tempo.
export function useDismiss(active: boolean, ref: RefObject<HTMLElement | null>, onClose: () => void) {
  useEffect(() => {
    if (!active) return
    const onPointerDown = (event: PointerEvent) => {
      if (ref.current && !ref.current.contains(event.target as Node)) onClose()
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [active, ref, onClose])
}
