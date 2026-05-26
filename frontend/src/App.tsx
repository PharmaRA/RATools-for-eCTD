import { useEffect, useState } from 'react'
import { BrowserRouter, useNavigate } from 'react-router-dom'
import { Spin, Tag } from 'antd'
import { Activity } from 'lucide-react'

import { AppRoutes } from './routes'

const RoutedAppShell = () => {
  const navigate = useNavigate()
  const [health, setHealth] = useState<'ok' | 'error' | 'loading'>('loading')

  useEffect(() => {
    fetch('/health')
      .then((res) => res.json())
      .then((data) => setHealth(data.status === 'ok' ? 'ok' : 'error'))
      .catch(() => setHealth('error'))
  }, [])

  return (
    <div className="min-h-screen bg-gray-50 flex flex-col">
      <header className="bg-slate-900 text-white p-4 shadow-md flex justify-between items-center z-10">
        <div className="flex items-center gap-2 cursor-pointer select-none" onClick={() => navigate('/')}>
          <Activity className="text-blue-400" />
          <h1 className="text-xl font-bold m-0 tracking-wide">RATools Admin</h1>
        </div>
        <div className="flex items-center gap-2 text-sm">
          <span className="text-gray-400">API Health:</span>
          {health === 'loading' ? <Spin size="small" /> : (
            health === 'ok' ? <Tag color="success" className="m-0 border-0">Online</Tag> : <Tag color="error" className="m-0 border-0">Offline</Tag>
          )}
        </div>
      </header>

      <main className="flex-1 p-6 overflow-auto max-w-7xl w-full mx-auto">
        <AppRoutes />
      </main>
    </div>
  )
}

export default function App() {
  return (
    <BrowserRouter>
      <RoutedAppShell />
    </BrowserRouter>
  )
}
