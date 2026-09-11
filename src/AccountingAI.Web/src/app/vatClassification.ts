// Rótulos pt-PT das enumerações da classificação de IVA por conta (F2-09).

export const VAT_RATES: { value: string; label: string }[] = [
  { value: 'Nenhuma', label: '— sem taxa —' },
  { value: 'Isento', label: 'Isento / não sujeito' },
  { value: 'Reduzida', label: 'Taxa reduzida' },
  { value: 'Intermedia', label: 'Taxa intermédia' },
  { value: 'Normal', label: 'Taxa normal' },
]

export const VAT_NATURES: { value: string; label: string }[] = [
  { value: 'Nenhuma', label: '— sem natureza —' },
  { value: 'TransmissoesInternas', label: 'Transmissões de bens (interno)' },
  { value: 'PrestacoesServicosInternas', label: 'Prestações de serviços (interno)' },
  { value: 'ExportacoesEIsentasComDeducao', label: 'Exportações / isentas com dedução' },
  { value: 'AquisicoesIntracomunitariasBens', label: 'Aquisições intracomunitárias de bens' },
  { value: 'AquisicoesIntracomunitariasServicos', label: 'Aquisições intracomunitárias de serviços' },
  { value: 'AutoliquidacaoOutras', label: 'Autoliquidação (outras)' },
  { value: 'OperacoesIsentasSemDeducao', label: 'Isentas sem direito à dedução' },
  { value: 'IvaDedutivelImobilizado', label: 'IVA dedutível — imobilizado' },
  { value: 'IvaDedutivelExistencias', label: 'IVA dedutível — existências' },
  { value: 'IvaDedutivelOutros', label: 'IVA dedutível — outros bens e serviços' },
  { value: 'RegularizacoesFavorSujeitoPassivo', label: 'Regularizações a favor do sujeito passivo' },
  { value: 'RegularizacoesFavorEstado', label: 'Regularizações a favor do Estado' },
]

export const VAT_REGIONS: { value: string; label: string }[] = [
  { value: 'Continente', label: 'Continente' },
  { value: 'Acores', label: 'Açores' },
  { value: 'Madeira', label: 'Madeira' },
]

const rateLabels = new Map(VAT_RATES.map((item) => [item.value, item.label]))
const natureLabels = new Map(VAT_NATURES.map((item) => [item.value, item.label]))
const regionLabels = new Map(VAT_REGIONS.map((item) => [item.value, item.label]))

export const vatRateLabel = (value: string) => rateLabels.get(value) ?? value
export const vatNatureLabel = (value: string) => natureLabels.get(value) ?? value
export const vatRegionLabel = (value: string) => regionLabels.get(value) ?? value
