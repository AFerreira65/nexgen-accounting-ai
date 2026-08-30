import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { AppLayout } from './app/AppLayout'
import { AccountPage } from './pages/AccountPage'
import { DashboardPage } from './pages/DashboardPage'
import { JournalPage } from './pages/JournalPage'
import { MovementPage } from './pages/MovementPage'
import { MovementsPage } from './pages/MovementsPage'
import { PlaceholderPage } from './pages/PlaceholderPage'
import { SettingsPage } from './pages/SettingsPage'
import { WorkContextProvider } from './app/WorkContext'

export function App() {
  return (
    <WorkContextProvider>
      <BrowserRouter>
        <Routes>
          <Route element={<AppLayout />}>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/movimentos" element={<MovementsPage />} />
            <Route path="/movimentos/:movementId" element={<MovementPage />} />
            <Route path="/contas/:accountCode" element={<AccountPage />} />
            <Route path="/diarios/:journalId" element={<JournalPage />} />
            <Route path="/contas" element={<PlaceholderPage title="Plano de contas" />} />
            <Route path="/diarios" element={<PlaceholderPage title="Diários" />} />
            <Route path="/documentos" element={<PlaceholderPage title="Documentos" />} />
            <Route path="/tarefas" element={<PlaceholderPage title="Tarefas" />} />
            <Route path="/definicoes" element={<SettingsPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </WorkContextProvider>
  )
}
