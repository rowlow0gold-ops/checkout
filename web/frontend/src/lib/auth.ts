import api from './api'

export interface AuthUser {
  role: 'super_admin' | 'admin'
  store_id: number | null
}

export async function login(username: string, password: string): Promise<AuthUser> {
  const form = new FormData()
  form.append('username', username)
  form.append('password', password)
  const { data } = await api.post('/auth/login', form)
  localStorage.setItem('token', data.access_token)
  return { role: data.role, store_id: data.store_id }
}

export function logout() {
  localStorage.removeItem('token')
  window.location.href = '/login'
}

export function getToken() {
  return localStorage.getItem('token')
}

export function getUser(): { username: string; role: 'super_admin' | 'admin'; store_id: number | null } | null {
  const token = getToken()
  if (!token) return null
  try {
    const payload = JSON.parse(atob(token.split('.')[1]))
    return { username: payload.username, role: payload.role, store_id: payload.store_id ?? null }
  } catch { return null }
}
