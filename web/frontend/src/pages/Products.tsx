import { useRef, useState, useMemo, useEffect } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Plus, Download, Upload, Pencil, Trash2, Search } from 'lucide-react'
import api, { downloadBlob } from '../lib/api'
import { getUser } from '../lib/auth'
import Pagination from '../components/Pagination'

interface Product { id: number; barcode: string; name: string; price: number; category: string }

function ProductModal({ product, onClose }: { product?: Product; onClose: () => void }) {
  const qc = useQueryClient()
  const [form, setForm] = useState({
    barcode:  product?.barcode  ?? '',
    name:     product?.name     ?? '',
    price:    product?.price    ?? '',
    category: product?.category ?? '',
  })

  const save = useMutation({
    mutationFn: (data: any) => product
      ? api.put(`/products/${product.id}`, data)
      : api.post('/products/', data),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['products'] }); onClose() },
  })

  return (
    <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-50">
      <div className="bg-gray-900 border border-gray-700 rounded-xl p-6 w-full max-w-md">
        <h2 className="text-sm font-semibold text-white mb-4">{product ? 'Edit Product' : 'Add Product'}</h2>
        <div className="space-y-3">
          {(['barcode', 'name', 'price', 'category'] as const).map(field => (
            <div key={field}>
              <label className="block text-xs text-gray-400 mb-1 capitalize">{field}</label>
              <input
                value={form[field] as string}
                onChange={e => setForm(f => ({ ...f, [field]: e.target.value }))}
                type={field === 'price' ? 'number' : 'text'}
                className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm text-white focus:outline-none focus:border-blue-500"
              />
            </div>
          ))}
        </div>
        <div className="flex gap-2 mt-5">
          <button onClick={onClose} className="flex-1 bg-gray-800 hover:bg-gray-700 text-gray-300 text-sm py-2 rounded-lg transition-colors">Cancel</button>
          <button
            onClick={() => save.mutate({ ...form, price: parseFloat(form.price as string) })}
            className="flex-1 bg-blue-600 hover:bg-blue-700 text-white text-sm py-2 rounded-lg transition-colors"
          >
            {save.isPending ? 'Saving...' : 'Save'}
          </button>
        </div>
      </div>
    </div>
  )
}

export default function ProductsPage() {
  const qc = useQueryClient()
  const user = getUser()
  const isSuperAdmin = user?.role === 'super_admin'
  const [modal, setModal] = useState<{ open: boolean; product?: Product }>({ open: false })
  const [importing, setImporting] = useState(false)
  const fileRef = useRef<HTMLInputElement>(null)
  const [search,   setSearch]   = useState('')
  const [category, setCategory] = useState('all')
  const [page,     setPage]     = useState(1)
  const [perPage,  setPerPage]  = useState(20)

  const { data = [], isLoading } = useQuery<Product[]>({
    queryKey: ['products'],
    queryFn: () => api.get('/products/').then(r => r.data),
  })

  const categories = useMemo(() => ['all', ...Array.from(new Set(data.map(p => p.category).filter(Boolean)))], [data])

  useEffect(() => { setPage(1) }, [search, category])

  const filtered = useMemo(() => {
    return data.filter(p => {
      if (search && !p.name.toLowerCase().includes(search.toLowerCase()) && !p.barcode.includes(search)) return false
      if (category !== 'all' && p.category !== category) return false
      return true
    })
  }, [data, search, category])

  const paginated = useMemo(
    () => filtered.slice((page - 1) * perPage, page * perPage),
    [filtered, page, perPage]
  )

  const del = useMutation({
    mutationFn: (id: number) => api.delete(`/products/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['products'] }),
  })

  async function handleImport(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    setImporting(true)
    try {
      const form = new FormData()
      form.append('file', file)
      const { data } = await api.post('/products/import', form)
      qc.invalidateQueries({ queryKey: ['products'] })
      alert(`Imported ${data.imported} products`)
    } catch {
      alert('Import failed')
    } finally {
      setImporting(false)
      if (fileRef.current) fileRef.current.value = ''
    }
  }

  return (
    <div className="p-6 space-y-4">
      {modal.open && <ProductModal product={modal.product} onClose={() => setModal({ open: false })} />}

      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-white">Products</h1>
          <p className="text-sm text-gray-500 mt-0.5">
            {isSuperAdmin ? 'Product catalog — Head Office' : 'Product catalog — view only'}
          </p>
        </div>
        <div className="flex gap-2">
          <button
            onClick={() => downloadBlob('/products/export/excel', 'products.xlsx')}
            className="flex items-center gap-2 bg-gray-800 hover:bg-gray-700 border border-gray-700 text-sm text-white px-3 py-2 rounded-lg transition-colors"
          >
            <Download size={14} /> Export
          </button>
          {isSuperAdmin && (
            <>
              <input ref={fileRef} type="file" accept=".xlsx" className="hidden" onChange={handleImport} />
              <button
                onClick={() => fileRef.current?.click()}
                disabled={importing}
                className="flex items-center gap-2 bg-gray-800 hover:bg-gray-700 border border-gray-700 text-sm text-white px-3 py-2 rounded-lg transition-colors disabled:opacity-50"
              >
                <Upload size={14} /> {importing ? 'Importing...' : 'Import'}
              </button>
              <button
                onClick={() => setModal({ open: true })}
                className="flex items-center gap-2 bg-blue-600 hover:bg-blue-700 text-sm text-white px-3 py-2 rounded-lg transition-colors"
              >
                <Plus size={14} /> Add Product
              </button>
            </>
          )}
        </div>
      </div>

      {/* Filters */}
      <div className="flex gap-2 items-center">
        <div className="relative">
          <Search size={13} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-gray-500" />
          <input
            value={search}
            onChange={e => setSearch(e.target.value)}
            placeholder="Search name or barcode…"
            className="bg-gray-800 border border-gray-700 rounded-lg pl-8 pr-3 py-1.5 text-sm text-white focus:outline-none focus:border-blue-500 w-52"
          />
        </div>
        <select
          value={category}
          onChange={e => setCategory(e.target.value)}
          className="bg-gray-800 border border-gray-700 rounded-lg px-3 py-1.5 text-sm text-white focus:outline-none focus:border-blue-500"
        >
          {categories.map(c => <option key={c} value={c}>{c === 'all' ? 'All categories' : c}</option>)}
        </select>
        {(search || category !== 'all') && (
          <button onClick={() => { setSearch(''); setCategory('all') }} className="text-xs text-gray-500 hover:text-white transition-colors px-2">
            Clear
          </button>
        )}
        <span className="text-xs text-gray-600 ml-auto">{filtered.length} of {data.length}</span>
      </div>

      <div className="bg-gray-900 border border-gray-800 rounded-xl overflow-hidden">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-800">
              {['Barcode', 'Name', 'Price', 'Category', ...(isSuperAdmin ? [''] : [])].map(h => (
                <th key={h} className="text-left text-xs text-gray-500 font-medium px-4 py-3">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {isLoading ? (
              <tr><td colSpan={5} className="text-center text-gray-500 py-8">Loading...</td></tr>
            ) : filtered.length === 0 ? (
              <tr><td colSpan={5} className="text-center text-gray-500 py-8">No products match</td></tr>
            ) : paginated.map(p => (
              <tr key={p.id} className="border-b border-gray-800/50 hover:bg-gray-800/30 transition-colors">
                <td className="px-4 py-3 font-mono text-xs text-gray-400">{p.barcode}</td>
                <td className="px-4 py-3 text-white">{p.name}</td>
                <td className="px-4 py-3 text-white font-medium">${parseFloat(p.price as any).toFixed(2)}</td>
                <td className="px-4 py-3">
                  <span className="bg-gray-800 text-gray-400 text-xs px-2 py-0.5 rounded-full">{p.category ?? '—'}</span>
                </td>
                {isSuperAdmin && (
                  <td className="px-4 py-3">
                    <div className="flex gap-1 justify-end">
                      <button onClick={() => setModal({ open: true, product: p })} className="p-1.5 text-gray-400 hover:text-white hover:bg-gray-700 rounded-lg transition-colors"><Pencil size={13} /></button>
                      <button onClick={() => del.mutate(p.id)} className="p-1.5 text-gray-400 hover:text-red-400 hover:bg-gray-700 rounded-lg transition-colors"><Trash2 size={13} /></button>
                    </div>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
        <Pagination
          total={filtered.length}
          page={page}
          perPage={perPage}
          onPage={setPage}
          onPerPage={n => { setPerPage(n); setPage(1) }}
        />
      </div>
    </div>
  )
}
