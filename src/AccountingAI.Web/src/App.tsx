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
import { OperatingMapPage, RatiosPage } from './pages/IndicatorsPages'
import { IesPage } from './pages/IesPage'
import { Modelo22Page } from './pages/Modelo22Page'
import { MonthlyEvolutionPage } from './pages/MonthlyEvolutionPage'
import { RecapitulativeStatementPage } from './pages/RecapitulativeStatementPage'
import { VatDeclarationPage } from './pages/VatDeclarationPage'
import { VatSettlementPage } from './pages/VatSettlementPage'
import { AtCommunicationPage } from './pages/AtCommunicationPage'
import { VatClassificationPage } from './pages/VatClassificationPage'
import { IrcEstimatePage } from './pages/IrcEstimatePage'
import { PartiesPage } from './pages/PartiesPage'
import { PartyBalancesByPartyPage, PartyStatementPage } from './pages/PartyLedgerPages'
import { VatCodesPage } from './pages/VatCodesPage'
import { FiscalTablesPage } from './pages/FiscalTablesPage'
import { FinancialStatementsPage } from './pages/FinancialStatementsPage'
import { DepreciationSchedulePage, FixedAssetsPage } from './pages/FixedAssetsPages'
import { SaftExportPage } from './pages/SaftExportPage'
import { AnalyticReportPage, CostCentersPage } from './pages/AnalyticPages'
import { EntryTemplatesPage } from './pages/EntryTemplatesPage'
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
            <Route path="/terceiros" element={<PartiesPage />} />
            <Route path="/terceiros/:partyId" element={<PartyStatementPage />} />
            <Route path="/saldos-terceiros" element={<PartyBalancesByPartyPage />} />
            <Route path="/lancamentos-tipo" element={<EntryTemplatesPage />} />
            <Route path="/centros-custo" element={<CostCentersPage />} />
            <Route path="/ativos-fixos" element={<FixedAssetsPage />} />
            <Route path="/mapa-depreciacoes" element={<DepreciationSchedulePage />} />
            <Route path="/mapa-analitico" element={<AnalyticReportPage />} />
            <Route path="/diarios" element={<JournalsPage />} />
            <Route path="/documentos" element={<DocumentTypesPage />} />
            <Route path="/series" element={<DocumentNumberSeriesPage />} />
            <Route path="/taxonomias" element={<TaxonomiesPage />} />
            <Route path="/importar" element={<SaftImportPage />} />
            <Route path="/exportar-saft" element={<SaftExportPage />} />
            <Route path="/demonstracoes" element={<FinancialStatementsPage />} />
            <Route path="/compras-efatura" element={<EFaturaPurchasesPage />} />
            <Route path="/cruzamento" element={<CrossCheckPage />} />
            <Route path="/comunicacao-at" element={<AtCommunicationPage />} />
            <Route path="/dicionario-de-dados" element={<DataDictionaryPage />} />
            <Route path="/evolucao" element={<MonthlyEvolutionPage />} />
            <Route path="/mapa-exploracao" element={<OperatingMapPage />} />
            <Route path="/racios" element={<RatiosPage />} />
            <Route path="/analises" element={<AnalysesPage />} />
            <Route path="/relatorios-gestao" element={<ManagementReportsPage />} />
            <Route path="/organizacao" element={<OrganizationPage />} />
            <Route path="/balancete" element={<TrialBalancePage />} />
            <Route path="/resultados" element={<IncomeStatementPage />} />
            <Route path="/balanco" element={<BalanceSheetPage />} />
            <Route path="/iva" element={<VatSummaryPage />} />
            <Route path="/apuramento-iva" element={<VatSettlementPage />} />
            <Route path="/declaracao-iva" element={<VatDeclarationPage />} />
            <Route path="/declaracao-recapitulativa" element={<RecapitulativeStatementPage />} />
            <Route path="/modelo-22" element={<Modelo22Page />} />
            <Route path="/ies" element={<IesPage />} />
            <Route path="/config-iva" element={<VatClassificationPage />} />
            <Route path="/codigos-iva" element={<VatCodesPage />} />
            <Route path="/tabelas-fiscais" element={<FiscalTablesPage />} />
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
