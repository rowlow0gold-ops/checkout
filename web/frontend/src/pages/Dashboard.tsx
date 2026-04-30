import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, BarChart, Bar } from 'recharts'
import { DollarSign, ShoppingCart, TrendingUp, Package } from 'lucide-react'
import api from '../lib/api'
import { getUser } from '../lib/auth'

const RANGE_OPTIONS = [
  { label: 'Last 30 days', key: '30d' },
  { label: 'This month',   key: 'month' },
  { label: 'This year',    key: 'year' },
  { label: 'Last 5 years', key: '5y' },
  { label: 'All',          key: 'all' },
] as const

type RangeKey = typeof RANGE_OPTIONS[number]['key']

function getDateParams(key: RangeKey): string {
  const today = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  const fmt = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth()+1)}-${pad(d.getDate())}`

  switch (key) {
    case '30d':
      return 'days=30'
    case 'month': {
      const from = new Date(today.getFullYear(), today.getMonth(), 1)
      return `from_date=${fmt(from)}&to_date=${fmt(today)}`
    }
    case 'year': {
      const from = new Date(today.getFullYear(), 0, 1)
      return `from_date=${fmt(from)}&to_date=${fmt(today)}`
    }
    case '5y': {
      const from = new Date(today.getFullYear() - 5, 0, 1)
      return `from_date=${fmt(from)}&to_date=${fmt(today)}`
    }
    case 'all':
      return 'from_date=2026-01-01'
  }
}

function tickFormatter(dateStr: string, rangeKey: RangeKey): string {
  // dateStr = "YYYY-MM-DD"
  if (rangeKey === '30d' || rangeKey === 'month') return dateStr.slice(5)  // MM-DD
  if (rangeKey === 'year') {
    const [, m, d] = dateStr.split('-')
    return d === '01' || d === '15' ? `${m}/${d}` : ''  // show only 1st and 15th
  }
  // 5y / all: show YYYY-MM
  return dateStr.slice(0, 7)
}

function StatCard({ label, value, icon: Icon, color }: { label: string; value: string; icon: any; color: string }) {
  return (
    <div className="bg-gray-900 border border-gray-800 rounded-xl p-5">
      <div className="flex items-center justify-between mb-3">
        <span className="text-xs text-gray-500 font-medium uppercase tracking-wide">{label}</span>
        <div className={`w-8 h-8 rounded-lg ${color} flex items-center justify-center`}>
          <Icon size={15} className="text-white" />
        </div>
      </div>
      <p className="text-2xl font-bold text-white">{value}</p>
    </div>
  )
}

export default function DashboardPage() {
  const user = getUser()
  const isSuperAdmin = user?.role === 'super_admin'
  const [rangeKey, setRangeKey] = useState<RangeKey>('30d')

  const { data: stores } = useQuery<{ id: number; name: string }[]>({
    queryKey: ['stores'],
    queryFn: () => api.get('/stores/').then(r => r.data),
    enabled: !isSuperAdmin,
  })

  const storeName = isSuperAdmin
    ? 'All Stores'
    : stores?.find(s => s.id === user?.store_id)?.name ?? `Store ${user?.store_id}`

  const { data: summary } = useQuery({
    queryKey: ['summary'],
    queryFn: () => api.get('/dashboard/summary').then(r => r.data),
    refetchInterval: 30000,
  })

  const { data: daily } = useQuery({
    queryKey: ['daily-sales', rangeKey],
    queryFn: () => api.get(`/dashboard/daily-sales?${getDateParams(rangeKey)}`).then(r => r.data),
    refetchInterval: 30000,
  })

  const { data: products } = useQuery<any[]>({
    queryKey: ['products'],
    queryFn: () => api.get('/products/').then(r => r.data),
  })

  const { data: topProducts } = useQuery({
    queryKey: ['top-products', rangeKey],
    queryFn: () => api.get(`/dashboard/top-products?limit=8&${getDateParams(rangeKey)}`).then(r => r.data),
    refetchInterval: 30000,
  })

  const fmt = (n: number) => `$${n?.toLocaleString('en-US', { minimumFractionDigits: 2 }) ?? '0.00'}`
  const rangeLabel = RANGE_OPTIONS.find(o => o.key === rangeKey)?.label ?? ''

  return (
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-white">Dashboard</h1>
        <p className="text-sm text-gray-500 mt-0.5">{storeName} — Sales overview</p>
      </div>

      {/* KPI cards */}
      <div className="grid grid-cols-2 xl:grid-cols-4 gap-4">
        <StatCard label="Today's Sales"      value={fmt(summary?.total_today)}         icon={DollarSign}   color="bg-blue-600" />
        <StatCard label="Monthly Sales"      value={fmt(summary?.total_month)}         icon={TrendingUp}   color="bg-green-600" />
        <StatCard label="Transactions Today" value={summary?.transactions_today ?? 0}  icon={ShoppingCart} color="bg-purple-600" />
        <StatCard label="Active Products"    value={String(products?.length ?? '—')}   icon={Package}      color="bg-orange-600" />
      </div>

      {/* Daily sales chart */}
      <div className="bg-gray-900 border border-gray-800 rounded-xl p-5">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-sm font-semibold text-white">Daily Sales — {rangeLabel}</h2>
          <select
            value={rangeKey}
            onChange={e => setRangeKey(e.target.value as RangeKey)}
            className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-1.5 text-xs text-white focus:outline-none focus:border-blue-500"
          >
            {RANGE_OPTIONS.map(o => (
              <option key={o.key} value={o.key}>{o.label}</option>
            ))}
          </select>
        </div>
        <ResponsiveContainer width="100%" height={220}>
          <AreaChart data={daily ?? []}>
            <defs>
              <linearGradient id="salesGrad" x1="0" y1="0" x2="0" y2="1">
                <stop offset="5%"  stopColor="#3b82f6" stopOpacity={0.3} />
                <stop offset="95%" stopColor="#3b82f6" stopOpacity={0} />
              </linearGradient>
            </defs>
            <CartesianGrid strokeDasharray="3 3" stroke="#1f2937" />
            <XAxis
              dataKey="date"
              tick={{ fontSize: 11, fill: '#6b7280' }}
              tickFormatter={d => tickFormatter(d, rangeKey)}
              interval={rangeKey === '30d' || rangeKey === 'month' ? 4 : rangeKey === 'year' ? 13 : 'preserveStartEnd'}
            />
            <YAxis tick={{ fontSize: 11, fill: '#6b7280' }} tickFormatter={v => `$${v}`} />
            <Tooltip
              contentStyle={{ background: '#111827', border: '1px solid #374151', borderRadius: 8 }}
              labelStyle={{ color: '#9ca3af', fontSize: 12 }}
              formatter={(v: any) => [`$${(+v).toFixed(2)}`, 'Sales']}
            />
            <Area type="monotone" dataKey="total" stroke="#3b82f6" fill="url(#salesGrad)" strokeWidth={2} dot={false} />
          </AreaChart>
        </ResponsiveContainer>
      </div>

      {/* Top products */}
      <div className="bg-gray-900 border border-gray-800 rounded-xl p-5">
        <h2 className="text-sm font-semibold text-white mb-4">Top Products by Revenue</h2>
        <ResponsiveContainer width="100%" height={220}>
          <BarChart data={topProducts ?? []} layout="vertical">
            <CartesianGrid strokeDasharray="3 3" stroke="#1f2937" horizontal={false} />
            <XAxis type="number" tick={{ fontSize: 11, fill: '#6b7280' }} tickFormatter={v => `$${v}`} />
            <YAxis type="category" dataKey="name" tick={{ fontSize: 11, fill: '#6b7280' }} width={120} />
            <Tooltip
              contentStyle={{ background: '#111827', border: '1px solid #374151', borderRadius: 8 }}
              formatter={(v: any) => [`$${(+v).toFixed(2)}`, 'Revenue']}
            />
            <Bar dataKey="revenue" fill="#3b82f6" radius={[0, 4, 4, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </div>
    </div>
  )
}
