import { Activity } from 'lucide-react'
import { Alert, Col, Form, Input, Modal, Row, Select, Tooltip, type FormInstance } from 'antd'

import { PathPicker } from '../../PathPicker'

type TemplateOption = {
  value: string
  label: string
}

type ApplicationFormModalsProps = {
  createOpen: boolean
  importOpen: boolean
  createForm: FormInstance
  importForm: FormInstance
  defaultTemplateKey?: string
  templateOptions: TemplateOption[]
  templatesLoading: boolean
  importingApplication: boolean
  onCreate: () => void
  onCreateCancel: () => void
  onImport: () => void
  onImportCancel: () => void
}

export const ApplicationFormModals = ({
  createOpen,
  importOpen,
  createForm,
  importForm,
  defaultTemplateKey,
  templateOptions,
  templatesLoading,
  importingApplication,
  onCreate,
  onCreateCancel,
  onImport,
  onImportCancel,
}: ApplicationFormModalsProps) => (
  <>
    <Modal
      title="新建申请"
      open={createOpen}
      onOk={onCreate}
      onCancel={onCreateCancel}
      destroyOnHidden
      width={600}
    >
      <Form form={createForm} layout="vertical" initialValues={{ ectdTemplateKey: defaultTemplateKey }}>
        <Row gutter={16}>
          <Col span={12}>
            <Form.Item name="applicationNumber" label="申请编号" rules={[{ required: true }]}>
              <Input placeholder="e.g. NDA123456" />
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item
              name="ectdTemplateKey"
              label="eCTD 模板"
              rules={[{ required: true, message: '请选择 eCTD 模板。' }]}
            >
              <Select loading={templatesLoading} options={templateOptions} placeholder="请选择 eCTD 模板" />
            </Form.Item>
          </Col>
        </Row>
        <Form.Item name="sponsorName" label="申办方名称" rules={[{ required: true }]}>
          <Input placeholder="e.g. Acme Pharma Ltd." />
        </Form.Item>
        <Form.Item
          name="workingDirectoryParentPath"
          label={(
            <span className="flex items-center gap-1">
              工作区父目录
              <Tooltip title="服务器上用于组装此申请文件夹的物理路径。">
                <Activity size={14} className="text-gray-400 cursor-help" />
              </Tooltip>
            </span>
          )}
          rules={[{ required: true, message: '请指定工作目录父路径。' }]}
        >
          <PathPicker placeholder="e.g. C:/eCTD/workspaces" />
        </Form.Item>
      </Form>
    </Modal>

    <Modal
      title="导入申请"
      open={importOpen}
      onOk={onImport}
      onCancel={onImportCancel}
      okText="导入"
      cancelText="取消"
      confirmLoading={importingApplication}
      destroyOnHidden
      width={680}
    >
      <Form form={importForm} layout="vertical" initialValues={{ ectdTemplateKey: defaultTemplateKey }}>
        <Form.Item
          name="workingDirectoryPath"
          label="工作目录路径"
          rules={[{ required: true, message: '请输入工作目录路径。' }]}
        >
          <PathPicker placeholder="e.g. C:/eCTD/workspaces/NDA123456" />
        </Form.Item>
        <Row gutter={16}>
          <Col span={8}>
            <Form.Item name="ectdTemplateKey" label="eCTD 模板" rules={[{ required: true, message: '请选择 eCTD 模板。' }]}>
              <Select loading={templatesLoading} options={templateOptions} placeholder="请选择 eCTD 模板" />
            </Form.Item>
          </Col>
          <Col span={16}>
            <Form.Item name="sponsorName" label="申办方名称" rules={[{ required: true, message: '请输入申办方名称。' }]}>
              <Input placeholder="e.g. Demo Sponsor" />
            </Form.Item>
          </Col>
        </Row>
        <Alert
          type="info"
          showIcon
          title="导入将从申请工作区目录读取序列，并解析每个序列的 index.xml。"
        />
      </Form>
    </Modal>
  </>
)
