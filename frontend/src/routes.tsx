import { Suspense, lazy } from 'react'
import { Spin } from 'antd'
import { Navigate, Route, Routes, useNavigate, useParams } from 'react-router-dom'

// 路由级代码分割：三个页面按需加载，避免全部打进首屏 chunk。
const ApplicationsPage = lazy(() =>
  import('./pages/ApplicationsPage').then((module) => ({ default: module.ApplicationsPage })))
const ApplicationDetailsPage = lazy(() =>
  import('./pages/ApplicationDetailsPage').then((module) => ({ default: module.ApplicationDetailsPage })))
const SequenceWorkspacePage = lazy(() =>
  import('./pages/SequenceWorkspacePage').then((module) => ({ default: module.SequenceWorkspacePage })))
const AuditLogsPage = lazy(() =>
  import('./pages/AuditLogsPage').then((module) => ({ default: module.AuditLogsPage })))

const RouteFallback = () => (
  <div className="flex justify-center py-16" role="status" aria-live="polite">
    <Spin />
  </div>
)

const ApplicationsRoute = () => {
  const navigate = useNavigate()

  return (
    <ApplicationsPage
      onSelectApp={(id) => navigate(`/applications/${id}`)}
    />
  )
}

const ApplicationDetailsRoute = () => {
  const navigate = useNavigate()
  const { applicationId } = useParams()

  if (!applicationId) {
    return <Navigate to="/" replace />
  }

  return (
    <ApplicationDetailsPage
      appId={applicationId}
      onBack={() => navigate('/')}
      onOpenWorkspace={(seq) => navigate(`/applications/${applicationId}/sequences/${seq}/workspace`)}
    />
  )
}

const SequenceWorkspaceRoute = () => {
  const navigate = useNavigate()
  const { applicationId, sequenceNumber } = useParams()

  if (!applicationId || !sequenceNumber) {
    return <Navigate to="/" replace />
  }

  return (
    <SequenceWorkspacePage
      appId={applicationId}
      seqNumber={sequenceNumber}
      onBack={() => navigate(`/applications/${applicationId}`)}
    />
  )
}

export const AppRoutes = () => {
  return (
    <Suspense fallback={<RouteFallback />}>
      <Routes>
        <Route path="/" element={<ApplicationsRoute />} />
        <Route path="/applications/:applicationId" element={<ApplicationDetailsRoute />} />
        <Route path="/applications/:applicationId/sequences/:sequenceNumber/workspace" element={<SequenceWorkspaceRoute />} />
        <Route path="/audit-logs" element={<AuditLogsPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </Suspense>
  )
}
