import { useState, useEffect } from 'react'
import { Sun, Moon } from 'lucide-react'
import { getToken } from '../lib/auth'
import { isAutoSyncEnabled } from '../lib/useTransactionEvents'

function parseToken() {
  const token = getToken()
  if (!token) return null
  try { return JSON.parse(atob(token.split('.')[1])) }
  catch { return null }
}

export default function SettingsPage() {
  const payload   = parseToken()
  const role      = payload?.role ?? '—'
  const roleLabel = role === 'super_admin' ? 'Head Office' : 'Branch Manager'

  const [theme, setTheme] = useState<'dark' | 'light'>(() =>
    (localStorage.getItem('theme') as 'dark' | 'light') ?? 'dark'
  )
  const [autoSync, setAutoSync] = useState(isAutoSyncEnabled)

  useEffect(() => {
    document.documentElement.classList.toggle('light', theme === 'light')
    localStorage.setItem('theme', theme)
  }, [theme])

  function toggleAutoSync() {
    const next = !autoSync
    setAutoSync(next)
    localStorage.setItem('autoSync', String(next))
  }

  return (
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-white">Settings</h1>
        <p className="text-sm text-gray-500 mt-0.5">System configuration</p>
      </div>

      {/* Account */}
      <div className="bg-gray-900 border border-gray-800 rounded-xl p-5 max-w-lg space-y-4">
        <h2 className="text-sm font-semibold text-white">Account</h2>
        <div>
          <label className="block text-xs text-gray-400 mb-1">Username</label>
          <input disabled value={payload?.username ?? '—'} className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm text-gray-400 cursor-not-allowed" />
        </div>
        <div>
          <label className="block text-xs text-gray-400 mb-1">Role</label>
          <input disabled value={roleLabel} className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm text-gray-400 cursor-not-allowed" />
        </div>
      </div>

      {/* Appearance */}
      <div className="bg-gray-900 border border-gray-800 rounded-xl p-5 max-w-lg space-y-5">
        <h2 className="text-sm font-semibold text-white">Appearance</h2>
        <div className="flex items-center justify-between">
          <div>
            <p className="text-sm text-white">Theme</p>
            <p className="text-xs text-gray-500 mt-0.5">{theme === 'dark' ? 'Dark mode' : 'Light mode'}</p>
          </div>
          <button
            onClick={() => setTheme(t => t === 'dark' ? 'light' : 'dark')}
            className={`relative w-14 h-7 rounded-full transition-colors ${theme === 'light' ? 'bg-blue-600' : 'bg-gray-700'}`}
          >
            <span className={`absolute top-1 w-5 h-5 rounded-full bg-white flex items-center justify-center shadow transition-all ${theme === 'light' ? 'left-8' : 'left-1'}`}>
              {theme === 'light'
                ? <Sun  size={11} className="text-blue-600" />
                : <Moon size={11} className="text-gray-600" />}
            </span>
          </button>
        </div>

        <div className="border-t border-gray-800 pt-5 flex items-center justify-between">
          <div>
            <p className="text-sm text-white">Auto sync</p>
            <p className="text-xs text-gray-500 mt-0.5">
              {autoSync ? 'Live — updates instantly when a sale is made' : 'Off — refresh manually to see new data'}
            </p>
          </div>
          <button
            onClick={toggleAutoSync}
            className={`relative w-14 h-7 rounded-full transition-colors ${autoSync ? 'bg-blue-600' : 'bg-gray-700'}`}
          >
            <span className={`absolute top-1 w-5 h-5 rounded-full bg-white shadow transition-all ${autoSync ? 'left-8' : 'left-1'}`} />
          </button>
        </div>
      </div>
    </div>
  )
}
