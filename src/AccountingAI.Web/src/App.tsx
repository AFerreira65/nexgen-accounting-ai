import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { AppLayout } from './app/AppLayout'
import { AccountPage } from './pages/AccountPage'
import { AccountsPage, DocumentTypesPage, JournalsPage, TaxonomiesPage } from './pages/AccountingMastersPages'
import { BalanceSheetPage } from './pages/BalanceSheetPage'
import { DashboardPage } from './pages/DashboardPage'
import { IncomeStatementPage } from './pages/IncomeStatementPage'
import { JournalPage } from './pages/JournalPage'
import { JournalReportPage } from './pages/JournalReportPage'
import { MovementPage } from './pages/MovementPage'
import { MovementsPage } from './pages/MovementsPage'
import { OrganizationPage } from './pages/OrganizationPage'
import { PlaceholderPage } from './pages/PlaceholderPage'
import { SaftImportPage } from './pages/SaftImportPage'
import { SettingsPage } from './pages/SettingsPage'
import { TrialBalancePage } from './pages/TrialBalancePage'
import { YearEndClosingPage } from './pages/YearEndClosingPage'
import { DocumentNumberSeriesPage } from './pages/DocumentNumberSeriesPage'
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
            <Route path="/contas" element={<AccountsPage />} />
            <Route path="/diarios" element={<JournalsPage />} />
            <Route path="/documentos" element={<DocumentTypesPage />} />
            <Route path="/series" element={<DocumentNumberSeriesPage />} />
            <Route path="/taxonomias" element={<TaxonomiesPage />} />
            <Route path="/importar" element={<SaftImportPage />} />
            <Route path="/organizacao" element={<OrganizationPage />} />
            <Route path="/balancete" element={<TrialBalancePage />} />
            <Route path="/resultados" element={<IncomeStatementPage />} />
            <Route path="/balanco" element={<BalanceSheetPage />} />
            <Route path="/apuramento" element={<YearEndClosingPage />} />
            <Route path="/livro-diario" element={<JournalReportPage />} />
            <Route path="/tarefas" element={<PlaceholderPage title="Tarefas" />} />
            <Route path="/definicoes" element={<SettingsPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </WorkContextProvider>
  )
}
