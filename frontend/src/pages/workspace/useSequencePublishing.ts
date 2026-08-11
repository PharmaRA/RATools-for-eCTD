import { useMemo, useState } from 'react'
import { Form, message } from 'antd'

import { createAndExecutePublishJob } from '../../publishActions'
import {
  getSequencePublishingMetadata,
  updateSequencePublishingMetadata,
} from '../../sequencePublishingMetadataActions'
import {
  getPublishReadiness,
  type PublishReadinessReport,
  validateSequence,
  type ValidationReport,
} from '../../validationActions'
import {
  buildPrePublishChecklistDisplay,
  buildPrePublishChecklistSummary,
  getPublishReadinessValidationIssues,
} from '../../prePublishChecklist'
import { getErrorMessage } from '../appShared'
import type { MetadataFormValues } from './PublishModal'
import { buildSequencePublishingMetadataUpdateRequest } from './publishingMetadataFormValues'
import { usePublishJobPolling } from './usePublishJobPolling'

export type SequencePublishingProviders = {
  validateSequenceProvider?: typeof validateSequence
  getPublishReadinessProvider?: typeof getPublishReadiness
  getSequencePublishingMetadataProvider?: typeof getSequencePublishingMetadata
  updateSequencePublishingMetadataProvider?: typeof updateSequencePublishingMetadata
  createAndExecutePublishJobProvider?: typeof createAndExecutePublishJob
}

type UseSequencePublishingOptions = SequencePublishingProviders & {
  appId: string
  seqNumber: string
}

export const useSequencePublishing = ({
  appId,
  seqNumber,
  validateSequenceProvider = validateSequence,
  getPublishReadinessProvider = getPublishReadiness,
  getSequencePublishingMetadataProvider = getSequencePublishingMetadata,
  updateSequencePublishingMetadataProvider = updateSequencePublishingMetadata,
  createAndExecutePublishJobProvider = createAndExecutePublishJob,
}: UseSequencePublishingOptions) => {
  const [publishing, setPublishing] = useState(false)
  const [isPublishModalOpen, setIsPublishModalOpen] = useState(false)
  const [validationResult, setValidationResult] = useState<ValidationReport | null>(null)
  const [publishReadiness, setPublishReadiness] = useState<PublishReadinessReport | null>(null)
  const [publishForm] = Form.useForm()
  const [publishMetadataForm] = Form.useForm<MetadataFormValues>()
  const {
    job: polledPublishJob,
    isPolling: isPublishPolling,
    error: publishPollingError,
    startPolling: startPublishPolling,
    stopPolling: stopPublishPolling,
  } = usePublishJobPolling()

  const validationSummary = useMemo(() => {
    if (!validationResult) {
      return null
    }

    return buildPrePublishChecklistSummary(validationResult)
  }, [validationResult])

  const openPublishModal = async () => {
    setPublishing(true)
    setValidationResult(null)
    setPublishReadiness(null)
    setIsPublishModalOpen(false)
    publishForm.resetFields()
    publishMetadataForm.resetFields()
    try {
      const sequenceNumber = String(seqNumber).trim()
      const nextValidationResult = await validateSequenceProvider({
        applicationId: appId,
        sequenceNumber,
      })

      const checklistSummary = buildPrePublishChecklistSummary(nextValidationResult)
      setValidationResult(nextValidationResult)
      if (!checklistSummary.canProceed) {
        return
      }

      const [metadata, readiness] = await Promise.all([
        getSequencePublishingMetadataProvider({
          applicationId: appId,
          sequenceNumber,
        }),
        getPublishReadinessProvider({
          applicationId: appId,
          sequenceNumber,
        }),
      ])

      publishMetadataForm.setFieldsValue({
        applicationType: metadata.applicationType || '',
        submissionType: metadata.submissionType,
        submissionSubtype: metadata.submissionSubtype || '',
        sequenceDescription: metadata.sequenceDescription,
        applicantName: metadata.applicantName,
        formType: metadata.formType || '',
        applicantContactName: metadata.applicantContactName || '',
        applicantContactType: metadata.applicantContactType || '',
        telephone: metadata.telephone || '',
        telephoneNumberType: metadata.telephoneNumberType || '',
        email: metadata.email || '',
      })
      setPublishReadiness(readiness)

      if (!readiness.isReady && readiness.missingMetadataFields.length === 0) {
        setValidationResult({
          ...nextValidationResult,
          isValid: false,
          issues: [
            ...nextValidationResult.issues,
            ...getPublishReadinessValidationIssues(readiness),
          ],
        })
        setPublishReadiness(null)
        return
      }

      setIsPublishModalOpen(true)
    } catch (error) {
      const errorMessage = getErrorMessage(error)
      setValidationResult({
        applicationId: appId,
        sequenceNumber: String(seqNumber).trim(),
        validationProfile: '校验 API',
        isValid: false,
        issues: [{ severity: 'Error', code: 'API_ERROR', message: errorMessage }],
        sectionMatches: [],
        lifecycleMatches: [],
      })
    } finally {
      setPublishing(false)
    }
  }

  const handlePublishModalCancel = () => {
    setIsPublishModalOpen(false)
    publishForm.resetFields()
    publishMetadataForm.resetFields()
    setPublishReadiness(null)
  }

  const triggerPublish = async () => {
    setPublishing(true)
    try {
      const sequenceNumber = String(seqNumber).trim()
      await publishForm.validateFields()

      if (publishReadiness && !publishReadiness.isReady && publishReadiness.missingMetadataFields.length > 0) {
        const metadataValues = await publishMetadataForm.validateFields()
        await updateSequencePublishingMetadataProvider(buildSequencePublishingMetadataUpdateRequest(
          appId,
          sequenceNumber,
          metadataValues,
        ))
        const updatedReadiness = await getPublishReadinessProvider({
          applicationId: appId,
          sequenceNumber,
        })
        setPublishReadiness(updatedReadiness)

        if (!updatedReadiness.isReady) {
          message.error('发布就绪度仍处于受阻状态。请先解决剩余发现项后再发布。')
          return
        }
      }

      const startedJob = await createAndExecutePublishJobProvider({
        applicationId: appId,
        sequenceNumber,
      })

      message.success('发布任务已启动，正在跟踪进度…')
      setIsPublishModalOpen(false)
      publishForm.resetFields()
      publishMetadataForm.resetFields()
      setPublishReadiness(null)
      if (startedJob?.id) {
        startPublishPolling(String(startedJob.id))
      }
    } catch (error) {
      message.error('发布失败：' + getErrorMessage(error))
    } finally {
      setPublishing(false)
    }
  }

  const validationDisplay = validationSummary ? buildPrePublishChecklistDisplay(validationSummary) : null

  return {
    publishing,
    isPublishModalOpen,
    validationSummary,
    validationIssueCountText: validationDisplay?.issueCountText || '',
    validationStatusText: validationDisplay?.statusText || '',
    publishReadiness,
    publishForm,
    publishMetadataForm,
    polledPublishJob,
    isPublishPolling,
    publishPollingError,
    openPublishModal,
    handlePublishModalCancel,
    triggerPublish,
    stopPublishPolling,
  }
}
