import { useCallback, useEffect, useState } from 'react'
import { BrowserRouter, useNavigate } from 'react-router-dom'
import { App as AntApp, ConfigProvider, Spin, Tag, theme } from 'antd'
import zhCN from 'antd/locale/zh_CN'
import { Activity, ScrollText } from 'lucide-react'

import { ErrorBoundary } from './ErrorBoundary'
import { checkHealth, type HealthStatus } from './healthActions'
import { messages } from './i18n/messages'
import { AppRoutes } from './routes'

// 健康探针轮询间隔：30s。既能及时反映后端掉线，又不至于给 /health 造成压力。
const HEALTH_POLL_INTERVAL_MS = 30_000

const RoutedAppShell = () => {
  const navigate = useNavigate()
  const [health, setHealth] = useState<HealthStatus | 'loading'>('loading')

  useEffect(() => {
    let cancelled = false

    const probe = async () => {
      const status = await checkHealth()
      if (!cancelled) {
        setHealth(status)
      }
    }

    void probe()
    const timer = window.setInterval(() => void probe(), HEALTH_POLL_INTERVAL_MS)

    return () => {
      cancelled = true
      window.clearInterval(timer)
    }
  }, [])

  const goHome = useCallback(() => navigate('/'), [navigate])
  const goAuditLogs = useCallback(() => navigate('/audit-logs'), [navigate])

  return (
    <div className="min-h-screen bg-gray-50 flex flex-col">
      <header className="bg-slate-900 text-white p-4 shadow-md flex justify-between items-center z-10">
        <button
          type="button"
          className="flex items-center gap-2 bg-transparent border-0 text-white cursor-pointer select-none p-0"
          onClick={goHome}
          aria-label="返回应用列表首页"
        >
          <Activity className="text-blue-400" aria-hidden="true" />
          <h1 className="text-xl font-bold m-0 tracking-wide">RATools Admin</h1>
        </button>
        <nav className="flex items-center gap-4 text-sm" aria-label="主导航">
          <button
            type="button"
            className="flex items-center gap-1 bg-transparent border-0 text-gray-300 hover:text-white cursor-pointer p-0"
            onClick={goAuditLogs}
          >
            <ScrollText size={16} aria-hidden="true" />
            {messages.auditLogs.navLabel}
          </button>
        </nav>
        <div className="flex items-center gap-2 text-sm" role="status" aria-live="polite">
          <span className="text-gray-400">API 状态：</span>
          {health === 'loading' ? <Spin size="small" aria-label="正在检测 API 状态" /> : (
            health === 'ok'
              ? <Tag color="success" className="m-0 border-0">在线</Tag>
              : <Tag color="error" className="m-0 border-0">离线</Tag>
          )}
        </div>
      </header>

      <main className="flex-1 p-6 overflow-auto max-w-7xl w-full mx-auto">
        <ErrorBoundary>
          <AppRoutes />
        </ErrorBoundary>
      </main>
    </div>
  )
}

export default function App() {
  return (
    <ConfigProvider
      locale={zhCN}
      // antd 默认会在两个 CJK 字符之间插入空格（"删除" 渲染成 "删 除"）。
      // 中文界面下这既影响排版，也让按钮文本匹配变得脆弱，故全局关闭。
      button={{ autoInsertSpace: false }}
      theme={{
        algorithm: theme.defaultAlgorithm,
        token: {
          colorPrimary: '#2563eb',
          borderRadius: 6,
        },
      }}
    >
      <AntApp>
        <BrowserRouter>
          <RoutedAppShell />
        </BrowserRouter>
      </AntApp>
    </ConfigProvider>
  )
}
