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
import { SaftImportPage } from './pages/SaftImportPage'
import { SettingsPage } from './pages/SettingsPage'
import { TasksPage } from './pages/TasksPage'
import { TrialBalancePage } from './pages/TrialBalancePage'
import { PartyBalancesPage } from './pages/PartyBalancesPage'
import { VatSummaryPage } from './pages/VatSummaryPage'
import { YearEndClosingPage } from './pages/YearEndClosingPage'
import { YearOpeningPage } from './pages/YearOpeningPage'
import { DocumentNumberSeriesPage } from './pages/DocumentNumberSeriesPage'
import { EFaturaPurchasesPage } from './pages/EFaturaPurchasesPage'
import { CrossCheckPage } from './pages/CrossCheckPage'
import { DataDictionaryPage } from './pages/DataDictionaryPage'
import { AnalysesPage } from './pages/AnalysesPage'
import { ManagementReportsPage } from './pages/ManagementReportsPage'
import { VatSettlementPage } from './pages/VatSettlementPage'
import { AtCommunicationPage } from './pages/AtCommunicationPage'
import { VatClassificationPage } from './pages/VatClassificationPage'
import { IrcEstimatePage } from './pages/IrcEstimatePage'
import { RecordProvider } from './app/RecordContext'
import { AuthGate } from './app/AuthGate'

export function App() {
  return (
    <AuthGate>
      <BrowserRouter>
        <RecordProvider>
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
            <Route path="/compras-efatura" element={<EFaturaPurchasesPage />} />
            <Route path="/cruzamento" element={<CrossCheckPage />} />
            <Route path="/comunicacao-at" element={<AtCommunicationPage />} />
            <Route path="/dicionario-de-dados" element={<DataDictionaryPage />} />
            <Route path="/analises" element={<AnalysesPage />} />
            <Route path="/relatorios-gestao" element={<ManagementReportsPage />} />
            <Route path="/organizacao" element={<OrganizationPage />} />
            <Route path="/balancete" element={<TrialBalancePage />} />
            <Route path="/resultados" element={<IncomeStatementPage />} />
            <Route path="/balanco" element={<BalanceSheetPage />} />
            <Route path="/iva" element={<VatSummaryPage />} />
            <Route path="/apuramento-iva" element={<VatSettlementPage />} />
            <Route path="/config-iva" element={<VatClassificationPage />} />
            <Route path="/estimativa-irc" element={<IrcEstimatePage />} />
            <Route path="/contas-correntes" element={<PartyBalancesPage />} />
            <Route path="/apuramento" element={<YearEndClosingPage />} />
            <Route path="/abertura" element={<YearOpeningPage />} />
            <Route path="/livro-diario" element={<JournalReportPage />} />
            <Route path="/tarefas" element={<TasksPage />} />
            <Route path="/definicoes" element={<SettingsPage />} />
          </Route>
        </Routes>
        </RecordProvider>
      </BrowserRouter>
    </AuthGate>
  )
}
