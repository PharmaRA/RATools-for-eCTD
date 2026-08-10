import { Alert, Form, Input, Modal, Tag, type FormInstance } from 'antd'

import { getPublishReadinessFindingSeverityTagColor } from '../../components/publishing/publishReadinessDisplay'
import type { PrePublishChecklistSummary } from '../../prePublishChecklist'
import type { PublishReadinessReport } from '../../validationActions'

export type MetadataFormValues = {
  applicationType?: string
  submissionType: string
  submissionSubtype?: string
  sequenceDescription: string
  applicantName: string
  formType?: string
  applicantContactName?: string
  applicantContactType?: string
  telephone?: string
  telephoneNumberType?: string
  email?: string
}

type PublishModalProps = {
  open: boolean
  publishing: boolean
  validationSummary: PrePublishChecklistSummary | null
  publishReadiness: PublishReadinessReport | null
  publishForm: FormInstance
  publishMetadataForm: FormInstance<MetadataFormValues>
  onOk: () => void
  onCancel: () => void
}

export const PublishModal = ({
  open,
  publishing,
  validationSummary,
  publishReadiness,
  publishForm,
  publishMetadataForm,
  onOk,
  onCancel,
}: PublishModalProps) => (
  <Modal
    title="发布序列"
    open={open}
    onCancel={onCancel}
    onOk={onOk}
    confirmLoading={publishing}
    destroyOnHidden
  >
    <Form form={publishForm} layout="vertical" requiredMark={false}>
      {validationSummary?.canProceed && (
        <Alert
          type="success"
          showIcon
          className="mb-3"
          title="发布前检查已通过"
          description={`发布前检查已通过。仍有 ${validationSummary.warningCount} 个警告供审阅者知悉。`}
        />
      )}
      {publishReadiness && !publishReadiness.isReady && (publishReadiness.missingMetadataFields?.length || 0) > 0 && (
        <div className="mb-3 flex flex-col gap-3">
          <Alert
            type="warning"
            showIcon
            title="发布就绪度受阻"
            description="请补全下方必填的发布元数据字段。执行发布时会先保存这些字段并重新评估就绪度。"
          />
          <div className="rounded border border-gray-200 bg-white/70 p-3 text-sm" data-testid="publish-readiness-findings">
            {publishReadiness.findings.map((finding) => (
              <div key={`${finding.code}-${finding.fieldName || 'none'}`} className="mb-2 last:mb-0">
                <Tag color={getPublishReadinessFindingSeverityTagColor(finding.severity)}>{finding.code}</Tag>
                {finding.fieldName && <Tag color="blue">{finding.fieldName}</Tag>}
                <span>{finding.recommendedAction}</span>
              </div>
            ))}
          </div>
          <Form form={publishMetadataForm} layout="vertical" requiredMark={false} component={false}>
            <Form.Item name="applicationType" label="申请类型" rules={[{ required: true, message: '申请类型为必填项。' }]}>
              <Input placeholder="例如 IND" />
            </Form.Item>
            <Form.Item name="submissionType" label="提交类型" rules={[{ required: true, message: '提交类型为必填项。' }]}>
              <Input placeholder="例如 original-application" />
            </Form.Item>
            <Form.Item name="submissionSubtype" label="提交子类型" rules={[{ required: true, message: '提交子类型为必填项。' }]}>
              <Input placeholder="例如 initial" />
            </Form.Item>
            <Form.Item name="sequenceDescription" label="序列描述" rules={[{ required: true, message: '序列描述为必填项。' }]}>
              <Input.TextArea rows={2} />
            </Form.Item>
            <Form.Item name="applicantName" label="申请人名称" rules={[{ required: true, message: '申请人名称为必填项。' }]}>
              <Input placeholder="例如 Acme Pharma" />
            </Form.Item>
            <Form.Item name="formType" label="表单类型">
              <Input placeholder="例如 356h" />
            </Form.Item>
            <Form.Item name="applicantContactName" label="申请人联系人姓名" rules={[{ required: true, message: '申请人联系人姓名为必填项。' }]}>
              <Input />
            </Form.Item>
            <Form.Item name="applicantContactType" label="申请人联系人类型" rules={[{ required: true, message: '申请人联系人类型为必填项。' }]}>
              <Input placeholder="例如 regulatory" />
            </Form.Item>
            <Form.Item name="telephone" label="电话" rules={[{ required: true, message: '电话为必填项。' }]}>
              <Input />
            </Form.Item>
            <Form.Item name="telephoneNumberType" label="电话号码类型" rules={[{ required: true, message: '电话号码类型为必填项。' }]}>
              <Input placeholder="例如 office" />
            </Form.Item>
            <Form.Item name="email" label="邮箱" rules={[{ required: true, message: '邮箱为必填项。' }]}>
              <Input type="email" />
            </Form.Item>
          </Form>
        </div>
      )}
    </Form>
  </Modal>
)
