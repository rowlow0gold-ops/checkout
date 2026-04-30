import { Outlet, NavLink } from 'react-router-dom'
import { LayoutDashboard, ShoppingCart, Package, Settings, LogOut } from 'lucide-react'
import { logout, getUser } from '../lib/auth'
import { useTransactionEvents } from '../lib/useTransactionEvents'

const nav = [
  { to: '/dashboard',    icon: LayoutDashboard, label: 'Dashboard' },
  { to: '/transactions', icon: ShoppingCart,    label: 'Transactions' },
  { to: '/products',     icon: Package,         label: 'Products' },
  { to: '/settings',     icon: Settings,        label: 'Settings' },
]

export default function Layout() {
  useTransactionEvents()
  const user = getUser()
  const roleLabel = user?.role === 'super_admin' ? 'Head Office' : 'Branch Manager'
  const roleBadgeColor = user?.role === 'super_admin' ? 'bg-blue-900/50 text-blue-400' : 'bg-green-900/50 text-green-400'

  return (
    <div className="flex h-screen bg-gray-950">
      {/* Sidebar */}
      <aside className="w-56 bg-gray-900 border-r border-gray-800 flex flex-col">
        <div className="p-5 border-b border-gray-800">
          <div className="flex items-center gap-2 mb-3">
            <div className="w-8 h-8 rounded-lg bg-blue-600 flex items-center justify-center font-bold text-sm">C</div>
            <span className="font-semibold text-sm text-white">Checkout Admin</span>
          </div>
          <div className="flex flex-col gap-1">
            <span className="text-xs text-gray-400">{user?.username}</span>
            <span className={`text-xs px-2 py-0.5 rounded-full w-fit ${roleBadgeColor}`}>{roleLabel}</span>
          </div>
        </div>
        <nav className="flex-1 p-3 space-y-1">
          {nav.map(({ to, icon: Icon, label }) => (
            <NavLink
              key={to}
              to={to}
              className={({ isActive }) =>
                `flex items-center gap-3 px-3 py-2 rounded-lg text-sm transition-colors ${
                  isActive
                    ? 'bg-blue-600 text-white'
                    : 'text-gray-400 hover:text-white hover:bg-gray-800'
                }`
              }
            >
              <Icon size={16} />
              {label}
            </NavLink>
          ))}
        </nav>
        <div className="p-3 border-t border-gray-800">
          <button
            onClick={() => logout()}
            className="flex items-center gap-3 px-3 py-2 rounded-lg text-sm text-gray-400 hover:text-white hover:bg-gray-800 w-full transition-colors"
          >
            <LogOut size={16} />
            Logout
          </button>
        </div>
      </aside>

      {/* Main */}
      <main className="flex-1 overflow-auto">
        <Outlet />
      </main>
    </div>
  )
}
