import { Navigate, Route, Routes, useNavigate, useParams } from 'react-router-dom'

import { ApplicationDetailsPage } from './pages/ApplicationDetailsPage'
import { ApplicationsPage } from './pages/ApplicationsPage'
import { SequenceWorkspacePage } from './pages/SequenceWorkspacePage'

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
    <Routes>
      <Route path="/" element={<ApplicationsRoute />} />
      <Route path="/applications/:applicationId" element={<ApplicationDetailsRoute />} />
      <Route path="/applications/:applicationId/sequences/:sequenceNumber/workspace" element={<SequenceWorkspaceRoute />} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
