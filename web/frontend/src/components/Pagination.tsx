import { ChevronLeft, ChevronRight } from 'lucide-react'

interface Props {
  total: number
  page: number
  perPage: number
  onPage: (p: number) => void
  onPerPage: (n: number) => void
}

const PER_PAGE_OPTIONS = [20, 50, 100]

export default function Pagination({ total, page, perPage, onPage, onPerPage }: Props) {
  const totalPages = Math.max(1, Math.ceil(total / perPage))
  const from = total === 0 ? 0 : (page - 1) * perPage + 1
  const to   = Math.min(page * perPage, total)

  function pageNumbers(): number[] {
    const count = Math.min(totalPages, 10)
    return Array.from({ length: count }, (_, i) => i + 1)
  }

  const btn = (active: boolean, disabled: boolean, onClick: () => void, children: React.ReactNode) => (
    <button
      onClick={onClick}
      disabled={disabled}
      className={`min-w-[32px] h-8 px-2 rounded-lg text-xs transition-colors
        ${active   ? 'bg-blue-600 text-white'
        : disabled ? 'text-gray-700 cursor-not-allowed'
                   : 'text-gray-400 hover:text-white hover:bg-gray-800'}`}
    >
      {children}
    </button>
  )

  return (
    <div className="flex items-center justify-between px-4 py-3 border-t border-gray-800">
      <span className="text-xs text-gray-500">
        {total === 0 ? 'No results' : `${from}–${to} of ${total}`}
      </span>

      <div className="flex items-center gap-1">
        {btn(false, page === 1, () => onPage(page - 1), <ChevronLeft size={13} />)}
        {pageNumbers().map(n =>
          btn(n === page, false, () => onPage(n), n)
        )}
        {btn(false, page === totalPages, () => onPage(page + 1), <ChevronRight size={13} />)}
      </div>

      <select
        value={perPage}
        onChange={e => { onPerPage(Number(e.target.value)); onPage(1) }}
        className="bg-gray-800 border border-gray-700 rounded-lg px-2 py-1 text-xs text-gray-400 focus:outline-none focus:border-blue-500"
      >
        {PER_PAGE_OPTIONS.map(n => <option key={n} value={n}>{n} / page</option>)}
      </select>
    </div>
  )
}
