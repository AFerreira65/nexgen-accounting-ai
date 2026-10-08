import { navSections, visibleNodes } from './navTree'

const leaves = (nodes: { to?: string; children?: unknown[] }[]): string[] =>
  nodes.flatMap((node) => node.to ? [node.to] : leaves((node.children ?? []) as { to?: string; children?: unknown[] }[]))

describe('visibleNodes', () => {
  it('shows everything to someone who can do everything', () => {
    const all = leaves(visibleNodes(navSections, () => true, true))
    expect(all).toContain('/ies')
    expect(all).toContain('/grupos-permissoes')
  })

  it('hides the screens without the view permission and the groups left empty', () => {
    const only = (key: string) => (permission: string) => permission === key
    const sections = visibleNodes(navSections, only('movements.view'), false)
    expect(leaves(sections)).toContain('/movimentos')
    expect(leaves(sections)).not.toContain('/ies')
    expect(leaves(sections)).not.toContain('/grupos-permissoes')
    // Sem nenhum ecrã visível, a secção «Exercício» desaparece da barra.
    expect(sections.map((section) => section.label)).not.toContain('Exercício')
  })

  it('keeps the screens that need no permission', () => {
    expect(leaves(visibleNodes(navSections, () => false, false))).toEqual(expect.arrayContaining(['/', '/painel-gabinete', '/organizacao']))
  })
})
