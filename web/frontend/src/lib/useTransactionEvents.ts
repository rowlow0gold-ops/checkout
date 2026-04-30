import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { getToken } from './auth'

export function isAutoSyncEnabled() {
  return localStorage.getItem('autoSync') !== 'false'
}

export function useTransactionEvents() {
  const qc = useQueryClient()

  useEffect(() => {
    if (!isAutoSyncEnabled()) return
    const token = getToken()
    if (!token) return

    const es = new EventSource(`/api/events/stream?token=${token}`)

    es.onmessage = (e) => {
      if (e.data === 'connected' || e.data.startsWith(':')) return
      qc.invalidateQueries({ queryKey: ['transactions'] })
      qc.invalidateQueries({ queryKey: ['summary'] })
      qc.invalidateQueries({ queryKey: ['daily-sales'] })
      qc.invalidateQueries({ queryKey: ['top-products'] })
    }

    es.onerror = () => es.close()

    return () => es.close()
  }, [qc])
}
