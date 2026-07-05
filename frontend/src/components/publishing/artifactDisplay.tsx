import { Tag } from 'antd'

export const renderArtifactExistsStatus = (exists?: boolean | null) => (
  exists ? <Tag color="green">Exists</Tag> : <Tag color="red">Missing</Tag>
)
