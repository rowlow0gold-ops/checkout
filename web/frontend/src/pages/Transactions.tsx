import { useState, useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Download, Search } from 'lucide-react'
import api, { downloadBlob } from '../lib/api'


const METHODS = ['all', 'cash', 'card', 'mobile']
const SUSPICION_LEVELS = ['all', 'high', 'medium', 'normal']
const PER_PAGE_OPTIONS = [20, 50, 100]

function SuspicionBadge({ score }: { score: number }) {
  if (score >= 61) return <span className="bg-red-900/50 text-red-400 text-xs px-2 py-0.5 rounded-full font-medium">{score} High</span>
  if (score >= 31) return <span className="bg-yellow-900/50 text-yellow-400 text-xs px-2 py-0.5 rounded-full">{score} Med</span>
  return <span className="bg-gray-800 text-gray-500 text-xs px-2 py-0.5 rounded-full">{score}</span>
}

export default function TransactionsPage() {
  // const user = getUser()

  const [search,    setSearch]    = useState('')
  const [method,    setMethod]    = useState('all')
  const [suspicion, setSuspicion] = useState('all')
  const [fromDate,  setFromDate]  = useState('')
  const [toDate,    setToDate]    = useState('')
  const [page,      setPage]      = useState(1)
  const [perPage,   setPerPage]   = useState(50)

  // Reset to page 1 when filters change
  useEffect(() => { setPage(1) }, [search, method, suspicion, fromDate, toDate, perPage])

  const params = new URLSearchParams({
    page: String(page),
    per_page: String(perPage),
    ...(search    && { search }),
    ...(method    !== 'all' && { method }),
    ...(suspicion !== 'all' && { suspicion }),
    ...(fromDate  && { from_date: fromDate }),
    ...(toDate    && { to_date: toDate + 'T23:59:59' }),
  })

  const { data, isLoading, isFetching } = useQuery<{
    total: number; page: number; per_page: number; pages: number; items: any[]
  }>({
    queryKey: ['transactions', params.toString()],
    queryFn: () => api.get(`/transactions/?${params}`).then(r => r.data),
    placeholderData: prev => prev,
  })

  const total = data?.total ?? 0
  const pages = data?.pages ?? 1
  const items = data?.items ?? []
  const from  = total === 0 ? 0 : (page - 1) * perPage + 1
  const to    = Math.min(page * perPage, total)

  const inputCls = 'bg-gray-800 border border-gray-700 rounded-lg px-3 py-1.5 text-sm text-white focus:outline-none focus:border-blue-500'

  // Build page list: always show first, last, and up to 5 around current
  function pageButtons() {
    if (pages <= 10) return Array.from({ length: pages }, (_, i) => i + 1)
    const around = new Set([1, pages, page - 1, page, page + 1, page - 2, page + 2].filter(p => p >= 1 && p <= pages))
    const sorted = [...around].sort((a, b) => a - b)
    const result: (number | '…')[] = []
    for (let i = 0; i < sorted.length; i++) {
      if (i > 0 && sorted[i] - sorted[i - 1] > 1) result.push('…')
      result.push(sorted[i])
    }
    return result
  }

  return (
    <div className="p-6 space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-white">Transactions</h1>
          <p className="text-sm text-gray-500 mt-0.5">
            {isLoading ? 'Loading…' : `${from}–${to} of ${total.toLocaleString()} records`}
            {isFetching && !isLoading && <span className="ml-2 text-gray-600">Updating…</span>}
          </p>
        </div>
        <button
          onClick={() => downloadBlob('/transactions/export/excel', 'transactions.xlsx')}
          className="flex items-center gap-2 bg-gray-800 hover:bg-gray-700 border border-gray-700 text-sm text-white px-3 py-2 rounded-lg transition-colors"
        >
          <Download size={14} /> Export Excel
        </button>
      </div>

      {/* Filters */}
      <div className="flex flex-wrap gap-2 items-center">
        <div className="relative">
          <Search size={13} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-gray-500" />
          <input
            value={search}
            onChange={e => setSearch(e.target.value)}
            placeholder="Search ID or store…"
            className={`${inputCls} pl-8 w-44`}
          />
        </div>

        <select value={method} onChange={e => setMethod(e.target.value)} className={inputCls}>
          {METHODS.map(m => <option key={m} value={m}>{m === 'all' ? 'All methods' : m}</option>)}
        </select>

        <select value={suspicion} onChange={e => setSuspicion(e.target.value)} className={inputCls}>
          {SUSPICION_LEVELS.map(l => <option key={l} value={l}>{l === 'all' ? 'All suspicion' : l + ' risk'}</option>)}
        </select>

        <input type="date" value={fromDate} onChange={e => setFromDate(e.target.value)} className={inputCls} />
        <span className="text-gray-600 text-sm">—</span>
        <input type="date" value={toDate} onChange={e => setToDate(e.target.value)} className={inputCls} />

        {(search || method !== 'all' || suspicion !== 'all' || fromDate || toDate) && (
          <button
            onClick={() => { setSearch(''); setMethod('all'); setSuspicion('all'); setFromDate(''); setToDate('') }}
            className="text-xs text-gray-500 hover:text-white transition-colors px-2"
          >
            Clear
          </button>
        )}
      </div>

      {/* Table */}
      <div className={`bg-gray-900 border border-gray-800 rounded-xl overflow-hidden transition-opacity ${isFetching ? 'opacity-70' : ''}`}>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-800">
              {['ID', 'Store', 'Terminal', 'Total', 'Payment', 'Suspicion', 'Date'].map(h => (
                <th key={h} className="text-left text-xs text-gray-500 font-medium px-4 py-3">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {isLoading ? (
              <tr><td colSpan={7} className="text-center text-gray-500 py-8">Loading…</td></tr>
            ) : items.length === 0 ? (
              <tr><td colSpan={7} className="text-center text-gray-500 py-8">No transactions match</td></tr>
            ) : items.map((t: any) => (
              <tr key={t.id} className="border-b border-gray-800/50 hover:bg-gray-800/30 transition-colors">
                <td className="px-4 py-3 text-gray-400">#{t.id}</td>
                <td className="px-4 py-3 text-white">{t.store_name}</td>
                <td className="px-4 py-3 text-gray-400">{t.terminal_id}</td>
                <td className="px-4 py-3 text-white font-medium">${parseFloat(t.total_amount).toFixed(2)}</td>
                <td className="px-4 py-3">
                  <span className="bg-gray-800 text-gray-300 text-xs px-2 py-0.5 rounded-full">{t.payment_method}</span>
                </td>
                <td className="px-4 py-3"><SuspicionBadge score={t.suspicion_score} /></td>
                <td className="px-4 py-3 text-gray-500 text-xs">{new Date(t.created_at).toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>

        {/* Pagination bar */}
        <div className="flex items-center justify-between px-4 py-3 border-t border-gray-800">
          <div className="flex items-center gap-2 text-xs text-gray-500">
            Rows per page:
            <select
              value={perPage}
              onChange={e => setPerPage(Number(e.target.value))}
              className="bg-gray-800 border border-gray-700 rounded px-2 py-1 text-white text-xs"
            >
              {PER_PAGE_OPTIONS.map(n => <option key={n} value={n}>{n}</option>)}
            </select>
          </div>

          <div className="flex items-center gap-1">
            <button
              onClick={() => setPage(p => Math.max(1, p - 1))}
              disabled={page === 1}
              className="px-2 py-1 text-xs text-gray-400 hover:text-white disabled:opacity-30 disabled:cursor-not-allowed"
            >
              ← Prev
            </button>

            {pageButtons().map((p, i) =>
              p === '…'
                ? <span key={`ellipsis-${i}`} className="px-2 text-gray-600 text-xs">…</span>
                : <button
                    key={p}
                    onClick={() => setPage(p as number)}
                    className={`w-7 h-7 text-xs rounded ${page === p ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white hover:bg-gray-800'}`}
                  >
                    {p}
                  </button>
            )}

            <button
              onClick={() => setPage(p => Math.min(pages, p + 1))}
              disabled={page === pages}
              className="px-2 py-1 text-xs text-gray-400 hover:text-white disabled:opacity-30 disabled:cursor-not-allowed"
            >
              Next →
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
