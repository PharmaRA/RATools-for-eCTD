import { apiFetch, ApiRequestError } from './apiClient';

export type DeleteEntity = 'application' | 'sequence';
export type DeleteMode = 'databaseOnly' | 'purgeWorkspace';

export type DeleteOutcome = {
  kind: 'success' | 'error';
  reason: 'success' | 'not_found' | 'conflict' | 'unexpected_error';
  message: string;
  shouldRefresh: boolean;
};

export type BatchDeleteItem = {
  key: string;
  label: string;
  url: string;
};

export type BatchDeleteItemResult = {
  key: string;
  label: string;
  outcome: DeleteOutcome;
};

export type BatchDeleteSummary = {
  entity: DeleteEntity;
  mode: DeleteMode;
  total: number;
  successCount: number;
  failureCount: number;
  results: BatchDeleteItemResult[];
};

type BatchDeleteResultsSource = Pick<BatchDeleteSummary, 'results'>;

export const getBatchDeleteResults = <TSource extends BatchDeleteResultsSource>(
  summary: TSource | null | undefined,
): BatchDeleteItemResult[] => summary?.results || [];

export const getFailedBatchDeleteResults = <TSource extends BatchDeleteResultsSource>(
  summary: TSource | null | undefined,
) => {
  return getBatchDeleteResults(summary).filter((result) => result.outcome.kind === 'error');
};

export const getSuccessfulBatchDeleteResults = <TSource extends BatchDeleteResultsSource>(
  summary: TSource | null | undefined,
) => {
  return getBatchDeleteResults(summary).filter((result) => result.outcome.kind === 'success');
};

export const buildApplicationBatchDeleteItems = (appIds: string[]): BatchDeleteItem[] => {
  return appIds.map((appId) => ({
    key: appId,
    label: appId,
    url: `/api/applications/${appId}`,
  }));
};

export const buildSequenceBatchDeleteItems = (appId: string, sequenceNumbers: string[]): BatchDeleteItem[] => {
  return sequenceNumbers.map((sequenceNumber) => ({
    key: sequenceNumber,
    label: sequenceNumber,
    url: `/api/applications/${appId}/sequences/${sequenceNumber}`,
  }));
};

const labels: Record<DeleteEntity, string> = {
  application: 'Application',
  sequence: 'Sequence',
};

const getNotFoundMessage = (entity: DeleteEntity, message: string) => {
  if (message === 'HTTP Error 404') {
    return `${labels[entity]} was not found.`;
  }

  return message || `${labels[entity]} was not found.`;
};

export const buildDeleteRequestUrl = (url: string, deleteMode: DeleteMode) => {
  return `${url}${url.includes('?') ? '&' : '?'}deleteMode=${encodeURIComponent(deleteMode)}`;
};

export const performDelete = async (
  entity: DeleteEntity,
  url: string,
  deleteMode: DeleteMode = 'databaseOnly',
  request: typeof apiFetch = apiFetch,
): Promise<DeleteOutcome> => {
  try {
    await request(buildDeleteRequestUrl(url, deleteMode), { method: 'DELETE' });

    return {
      kind: 'success',
      reason: 'success',
      message: `${labels[entity]} deleted successfully.`,
      shouldRefresh: true,
    };
  } catch (error) {
    const status = error instanceof ApiRequestError ? error.status : undefined;
    const rawMessage = error instanceof Error ? error.message : 'Unexpected error.';

    if (status === 404 || status === 409) {
      return {
        kind: 'error',
        reason: status === 404 ? 'not_found' : 'conflict',
        message: status === 404 ? getNotFoundMessage(entity, rawMessage) : rawMessage,
        shouldRefresh: true,
      };
    }

    return {
      kind: 'error',
      reason: 'unexpected_error',
      message: `Failed to delete ${entity}: ${rawMessage}`,
      shouldRefresh: false,
    };
  }
};

export const performBatchDelete = async (
  entity: DeleteEntity,
  deleteMode: DeleteMode,
  items: BatchDeleteItem[],
  request: typeof apiFetch = apiFetch,
  onProgress?: (result: BatchDeleteItemResult) => void,
): Promise<BatchDeleteSummary> => {
  const results: BatchDeleteItemResult[] = [];

  for (const item of items) {
    const outcome = await performDelete(entity, item.url, deleteMode, request);
    const result: BatchDeleteItemResult = {
      key: item.key,
      label: item.label,
      outcome,
    };

    results.push(result);
    onProgress?.(result);
  }

  const successCount = getSuccessfulBatchDeleteResults({ results }).length;

  return {
    entity,
    mode: deleteMode,
    total: items.length,
    successCount,
    failureCount: items.length - successCount,
    results,
  };
};
