import { apiFetch, ApiRequestError } from './apiClient';

export type DeleteEntity = 'application' | 'sequence';
export type DeleteMode = 'databaseOnly' | 'purgeWorkspace';

export type DeleteOutcome = {
  kind: 'success' | 'error';
  reason: 'success' | 'not_found' | 'conflict' | 'unexpected_error';
  message: string;
  shouldRefresh: boolean;
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

export const performDelete = async (
  entity: DeleteEntity,
  url: string,
  deleteMode: DeleteMode = 'databaseOnly',
  request: typeof apiFetch = apiFetch,
): Promise<DeleteOutcome> => {
  try {
    const requestUrl = `${url}${url.includes('?') ? '&' : '?'}deleteMode=${encodeURIComponent(deleteMode)}`;
    await request(requestUrl, { method: 'DELETE' });

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
