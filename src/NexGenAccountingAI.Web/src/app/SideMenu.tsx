import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useCallback, useEffect, useLayoutEffect, useRef, useState, type PointerEvent, type ReactNode, type RefObject } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { isNodeActive, navSections, visibleNodes, type NavNode } from './navTree'
import { useDismiss } from './useDismiss'
import { useWorkContext } from './WorkContext'

type Side = 'left' | 'right'

const CLOSE_DELAY_MS = 260

// Um menu que abre ao lado do item que o originou. É `fixed` para não ser
// cortado pela barra; vai para o lado oposto ao da barra, inverte se não houver
// espaço e fica sempre dentro do ecrã na vertical.
function Flyout({ anchor, side, nested, children }: { anchor: RefObject<HTMLElement | null>; side: Side; nested: boolean; children: ReactNode }) {
  const ref = useRef<HTMLUListElement>(null)
  useLayoutEffect(() => {
    const element = ref.current
    const row = anchor.current
    if (!element || !row) return
    const rect = row.getBoundingClientRect()
    const { offsetWidth: width, offsetHeight: height } = element
    const gap = nested ? 4 : 8
    let left = side === 'right' ? rect.left - width - gap : rect.right + gap
    if (left < 8) left = rect.right + gap
    else if (left + width > window.innerWidth - 8) left = rect.left - width - gap
    const top = Math.max(8, Math.min(rect.top - (nested ? 6 : 0), window.innerHeight - height - 8))
    element.style.left = `${Math.max(8, left)}px`
    element.style.top = `${top}px`
    element.style.visibility = 'visible'
  }, [anchor, side, nested])
  return <ul className="flyout" ref={ref}>{children}</ul>
}

type Context = {
  side: Side
  pathname: string
  openPath: string[]
  setOpenPath: (updater: (current: string[]) => string[]) => void
  onNavigate: () => void
}

function MenuNode({ node, depth, path, context }: { node: NavNode; depth: number; path: string[]; context: Context }) {
  const { side, pathname, openPath, setOpenPath, onNavigate } = context
  const buttonRef = useRef<HTMLButtonElement>(null)
  const Icon = node.icon
  const isGroup = Boolean(node.children)
  const isOpen = isGroup && openPath[depth] === node.label && path.every((part, index) => openPath[index] === part)
  const active = isNodeActive(node, pathname)
  const Chevron = side === 'right' ? ChevronLeft : ChevronRight

  // Só o rato abre ao passar por cima; no toque abre-se com um toque (e o
  // evento de entrada simulado depois do toque não fecha o que acabou de abrir).
  const hover = (event: PointerEvent) => {
    if (event.pointerType !== 'mouse') return
    setOpenPath((current) => isGroup ? [...path, node.label] : current.length > depth ? current.slice(0, depth) : current)
  }

  const content = <>
    {Icon && <Icon size={depth === 0 ? 21 : 17} strokeWidth={1.8} />}
    <span>{node.label}</span>
    {isGroup && depth > 0 && <Chevron className="flyout-chevron" size={15} />}
  </>

  return <li className={depth === 0 ? 'rail-item' : 'flyout-item'} onPointerEnter={hover}>
    {isGroup
      ? <button type="button" ref={buttonRef} className={`menu-row${active ? ' active' : ''}${isOpen ? ' open' : ''}`} aria-haspopup="true" aria-expanded={isOpen} title={node.label} onClick={() => setOpenPath(() => [...path, node.label])}>{content}</button>
      : <NavLink to={node.to!} end={node.end} title={node.label} className={`menu-row${active ? ' active' : ''}`} onClick={onNavigate}>{content}</NavLink>}
    {isOpen && <Flyout anchor={buttonRef} side={side} nested={depth > 0}>
      {node.children!.map((child) => <MenuNode key={child.label} node={child} depth={depth + 1} path={[...path, node.label]} context={context} />)}
    </Flyout>}
  </li>
}

// A barra com as secções principais: passa-se o rato e abre o menu da secção
// (e, nele, os submenus). `side` é o lado em que a barra está; os menus abrem
// para o lado contrário.
export function SideMenu({ side, onNavigate }: { side: Side; onNavigate: () => void }) {
  const { pathname } = useLocation()
  const { can, activeCompany } = useWorkContext()
  const sections = visibleNodes(navSections, can, activeCompany.canManage ?? true)
  const [openPath, setOpenPath] = useState<string[]>([])
  const navRef = useRef<HTMLElement>(null)
  const timer = useRef<number | undefined>(undefined)
  const closeAll = useCallback(() => setOpenPath([]), [])
  useDismiss(openPath.length > 0, navRef, closeAll)
  useEffect(() => { closeAll() }, [pathname, closeAll])
  useEffect(() => () => window.clearTimeout(timer.current), [])

  const context: Context = { side, pathname, openPath, setOpenPath, onNavigate: () => { closeAll(); onNavigate() } }

  return <nav
    ref={navRef}
    className="primary-nav"
    aria-label="Navegação principal"
    onPointerEnter={(event) => { if (event.pointerType === 'mouse') window.clearTimeout(timer.current) }}
    onPointerLeave={(event) => { if (event.pointerType === 'mouse') { window.clearTimeout(timer.current); timer.current = window.setTimeout(closeAll, CLOSE_DELAY_MS) } }}
  >
    <ul className="rail">
      {sections.map((section) => <MenuNode key={section.label} node={section} depth={0} path={[]} context={context} />)}
    </ul>
  </nav>
}
