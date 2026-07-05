import { Alert, Form, Input, Modal, Tag, type FormInstance } from 'antd'

import { PathPicker } from '../../PathPicker'
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
    title="Publish Sequence"
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
          title="Pre-publish checks passed"
          description={`Pre-publish checks passed. ${validationSummary.warningCount} warning(s) remain for reviewer awareness.`}
        />
      )}
      {publishReadiness && !publishReadiness.isReady && (publishReadiness.missingMetadataFields?.length || 0) > 0 && (
        <div className="mb-3 flex flex-col gap-3">
          <Alert
            type="warning"
            showIcon
            title="Publish readiness is blocked"
            description="Complete the required publishing metadata fields below. The publish action will save them and rerun readiness before execution."
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
            <Form.Item name="applicationType" label="Application Type" rules={[{ required: true, message: 'Application type is required.' }]}>
              <Input placeholder="e.g. IND" />
            </Form.Item>
            <Form.Item name="submissionType" label="Submission Type" rules={[{ required: true, message: 'Submission type is required.' }]}>
              <Input placeholder="e.g. original-application" />
            </Form.Item>
            <Form.Item name="submissionSubtype" label="Submission Subtype" rules={[{ required: true, message: 'Submission subtype is required.' }]}>
              <Input placeholder="e.g. initial" />
            </Form.Item>
            <Form.Item name="sequenceDescription" label="Sequence Description" rules={[{ required: true, message: 'Sequence description is required.' }]}>
              <Input.TextArea rows={2} />
            </Form.Item>
            <Form.Item name="applicantName" label="Applicant Name" rules={[{ required: true, message: 'Applicant name is required.' }]}>
              <Input placeholder="e.g. Acme Pharma" />
            </Form.Item>
            <Form.Item name="formType" label="Form Type">
              <Input placeholder="e.g. 356h" />
            </Form.Item>
            <Form.Item name="applicantContactName" label="Applicant Contact Name" rules={[{ required: true, message: 'Applicant contact name is required.' }]}>
              <Input />
            </Form.Item>
            <Form.Item name="applicantContactType" label="Applicant Contact Type" rules={[{ required: true, message: 'Applicant contact type is required.' }]}>
              <Input placeholder="e.g. regulatory" />
            </Form.Item>
            <Form.Item name="telephone" label="Telephone" rules={[{ required: true, message: 'Telephone is required.' }]}>
              <Input />
            </Form.Item>
            <Form.Item name="telephoneNumberType" label="Telephone Number Type" rules={[{ required: true, message: 'Telephone number type is required.' }]}>
              <Input placeholder="e.g. office" />
            </Form.Item>
            <Form.Item name="email" label="Email" rules={[{ required: true, message: 'Email is required.' }]}>
              <Input type="email" />
            </Form.Item>
          </Form>
        </div>
      )}
      <Form.Item
        name="outputDirectoryPath"
        label="Export Directory"
        rules={[{ required: true, message: 'Export directory is required.' }]}
      >
        <PathPicker placeholder="e.g. C:/eCTD/exports" />
      </Form.Item>
    </Form>
  </Modal>
)
