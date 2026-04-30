import { useEffect, useRef } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { getToken } from './auth'

export function isAutoSyncEnabled() {
  return localStorage.getItem('autoSync') !== 'false'
}

export function useTransactionEvents() {
  const qc      = useQueryClient()
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const esRef    = useRef<EventSource | null>(null)

  useEffect(() => {
    if (!isAutoSyncEnabled()) return

    let stopped = false

    function connect() {
      if (stopped) return
      const token = getToken()
      if (!token) return

      const es = new EventSource(`/api/events/stream?token=${token}`)
      esRef.current = es

      es.onmessage = (e) => {
        // skip keepalive pings and the initial connected message
        if (!e.data || e.data === 'connected') return
        qc.invalidateQueries({ queryKey: ['transactions'] })
        qc.invalidateQueries({ queryKey: ['summary'] })
        qc.invalidateQueries({ queryKey: ['daily-sales'] })
        qc.invalidateQueries({ queryKey: ['top-products'] })
      }

      es.onerror = () => {
        es.close()
        esRef.current = null
        if (!stopped) {
          // back off 5 s before reconnecting
          timerRef.current = setTimeout(connect, 5000)
        }
      }
    }

    connect()

    return () => {
      stopped = true
      if (timerRef.current) clearTimeout(timerRef.current)
      esRef.current?.close()
    }
  }, [qc])
}
