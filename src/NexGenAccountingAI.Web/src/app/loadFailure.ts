import { useSyncExternalStore } from 'react'

// Falha ao ler os dados de um ecrã no servidor. Os ecrãs de mapas calculavam, nesse
// caso, a partir dos movimentos em memória — que deixaram de estar todos carregados —
// e mostravam números errados sem aviso. Agora ficam vazios e sinalizam aqui; a moldura
// da aplicação mostra o aviso até se mudar de ecrã.

let failed = false
const listeners = new Set<() => void>()
const emit = () => listeners.forEach((listener) => listener())

export function signalLoadFailure() {
  if (!failed) { failed = true; emit() }
}

export function clearLoadFailure() {
  if (failed) { failed = false; emit() }
}

export const useLoadFailure = () => useSyncExternalStore(
  (listener) => { listeners.add(listener); return () => { listeners.delete(listener) } },
  () => failed,
)
