import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'

/**
 * O registo em que o utilizador está posicionado — publicado pelos ecrãs de
 * detalhe e consumido pelo menu «Opções» («Sobre o Registo»). `table` é o nome
 * lógico que o endpoint `record-info` conhece (ex.: `movimentos`, `contas`).
 */
export type FocusedRecord = { table: string; id: string; label: string }

type RecordSetter = (record: FocusedRecord | null) => void

// Dois contextos: o do valor muda a cada registo; o do setter (vindo do
// useState) é estável, pelo que quem só publica não re-executa em cadeia.
const RecordValueContext = createContext<FocusedRecord | null>(null)
const RecordSetterContext = createContext<RecordSetter>(() => {})

export function RecordProvider({ children }: { children: ReactNode }) {
  const [record, setRecord] = useState<FocusedRecord | null>(null)
  return (
    <RecordSetterContext.Provider value={setRecord}>
      <RecordValueContext.Provider value={record}>{children}</RecordValueContext.Provider>
    </RecordSetterContext.Provider>
  )
}

export function useFocusedRecord(): FocusedRecord | null {
  return useContext(RecordValueContext)
}

/**
 * Um ecrã de detalhe chama isto para publicar o registo em foco; limpa ao sair
 * (ou quando a identidade muda). Fora de um <RecordProvider> não faz nada.
 */
export function usePublishRecord(table: string | undefined, id: string | undefined, label?: string) {
  const setRecord = useContext(RecordSetterContext)
  useEffect(() => {
    setRecord(table && id ? { table, id, label: label ?? id } : null)
    return () => setRecord(null)
  }, [setRecord, table, id, label])
}
