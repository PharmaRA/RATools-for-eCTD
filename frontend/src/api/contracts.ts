import type {
  ApplicationDto,
  ApplicationImportIssueDto,
  ApplicationImportResultDto,
  ApplicationPublishHistoryDto,
  ApplicationPublishHistoryEntryDto,
  ApplicationPublishHistoryLifecycleSummaryDto,
  ApplicationPublishHistoryReadinessSummaryDto,
  AuditLogDto,
  AuditLogPageDto,
  CreateApplicationRequestBody,
  CreateSequenceRequestBody,
  DirectoryBrowseEntry as DirectoryBrowseEntryDto,
  DirectoryBrowseResult as DirectoryBrowseResultDto,
  DirectoryResolutionResult as DirectoryResolutionResultDto,
  DocumentDto,
  DocumentPlacementDto,
  EctdStructureDto,
  EctdStructureNodeDto,
  EctdTemplateDto,
  ImportApplicationRequestBody,
  PublishArtifactSummaryDto,
  SequenceDto,
} from './generated'

type NullablePartial<T> = {
  [Property in keyof T]?: T[Property] | null
}

export type ApplicationContract = ApplicationDto
export type ApplicationPublishHistoryContract = ApplicationPublishHistoryDto
export type AuditLogEntry = AuditLogDto
export type AuditLogPage = AuditLogPageDto
export type CreateApplicationContract = CreateApplicationRequestBody
export type CreateSequenceContract = CreateSequenceRequestBody
export type DirectoryBrowseEntry = DirectoryBrowseEntryDto
export type DirectoryBrowseResult = DirectoryBrowseResultDto
export type DirectoryResolutionResult = DirectoryResolutionResultDto
export type DocumentContract = DocumentDto
export type DocumentPlacementContract = DocumentPlacementDto
export type EctdStructureContract = EctdStructureDto
export type EctdTemplateContract = EctdTemplateDto
export type ImportApplicationContract = ImportApplicationRequestBody
export type ImportApplicationIssue = ApplicationImportIssueDto
export type ImportApplicationResult = ApplicationImportResultDto

export type EctdTemplateOption = Pick<EctdTemplateDto, 'key' | 'displayName' | 'region'>
  & Partial<Omit<EctdTemplateDto, 'key' | 'displayName' | 'region'>>

export type SequenceSummary = Pick<SequenceDto, 'sequenceNumber'>
  & Partial<Omit<SequenceDto, 'sequenceNumber'>>

export type Application = Pick<ApplicationDto, 'id' | 'applicationNumber' | 'sponsorName'>
  & Partial<Omit<ApplicationDto, 'id' | 'applicationNumber' | 'sponsorName' | 'sequences'>>
  & { sequences: SequenceSummary[] }

export type DocumentRecord = Pick<DocumentDto, 'id' | 'fileName' | 'storagePath'>
  & Partial<Omit<DocumentDto, 'id' | 'fileName' | 'storagePath'>>

export type DocumentPlacementRecord = Pick<
  DocumentPlacementDto,
  'id' | 'documentId' | 'applicationId' | 'sequenceNumber' | 'ctdSection' | 'operation'
> & Partial<Omit<
  DocumentPlacementDto,
  'id' | 'documentId' | 'applicationId' | 'sequenceNumber' | 'ctdSection' | 'operation'
>>

export type EctdStructureNode = EctdStructureNodeDto
export type EctdStructureResponse = EctdStructureDto
export type LifecycleSummary = NullablePartial<ApplicationPublishHistoryLifecycleSummaryDto>
export type PublishReadinessSummary = Partial<ApplicationPublishHistoryReadinessSummaryDto>
export type ArtifactSummary = Partial<PublishArtifactSummaryDto>

type PublishHistoryEntryOptionalFields = Omit<
  Partial<ApplicationPublishHistoryEntryDto>,
  | 'publishJobId'
  | 'sequenceNumber'
  | 'status'
  | 'lifecycleSummary'
  | 'publishReadiness'
  | 'artifactSummary'
>

export type PublishHistoryEntry = Pick<
  ApplicationPublishHistoryEntryDto,
  'publishJobId' | 'sequenceNumber' | 'status'
> & PublishHistoryEntryOptionalFields & {
  lifecycleSummary?: LifecycleSummary | null
  publishReadiness?: PublishReadinessSummary | null
  artifactSummary?: ArtifactSummary | null
}
