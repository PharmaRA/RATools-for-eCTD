import { afterEach, describe, expect, it, vi } from 'vitest';

import { ApiRequestError } from './apiClient';
import {
  buildApplicationBatchDeleteItems,
  buildApplicationDeleteUrl,
  buildDeleteRequestUrl,
  buildSequenceBatchDeleteItems,
  buildSequenceDeleteUrl,
  getBatchDeleteResults,
  getFailedBatchDeleteResults,
  getSuccessfulBatchDeleteResults,
  performBatchDelete,
  performDelete,
} from './deleteActions';

describe('deleteActions', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('builds delete request URLs with delete mode query parameters', () => {
    expect(buildDeleteRequestUrl('/api/applications/app-1', 'databaseOnly')).toBe(
      '/api/applications/app-1?deleteMode=databaseOnly',
    );
    expect(buildDeleteRequestUrl('/api/applications/app-2?force=true', 'purgeWorkspace')).toBe(
      '/api/applications/app-2?force=true&deleteMode=purgeWorkspace',
    );
  });

  it('builds application delete URLs from application ids', () => {
    expect(buildApplicationDeleteUrl('app-1')).toBe('/api/applications/app-1');
  });

  it('builds sequence delete URLs from application ids and sequence numbers', () => {
    expect(buildSequenceDeleteUrl('app-1', '0001')).toBe('/api/applications/app-1/sequences/0001');
  });

  it('returns a success message for application deletion', async () => {
    const request = vi.fn().mockResolvedValue(undefined);

    await expect(performDelete('application', '/api/applications/app-1', 'databaseOnly', request)).resolves.toEqual({
      kind: 'success',
      reason: 'success',
      message: 'Application deleted successfully.',
      shouldRefresh: true,
    });

    expect(request).toHaveBeenCalledWith('/api/applications/app-1?deleteMode=databaseOnly', { method: 'DELETE' });
  });

  it('uses apiFetch by default for successful deletes', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      status: 204,
      json: vi.fn().mockRejectedValue(new Error('No body')),
    }));

    await expect(performDelete('application', '/api/applications/app-1')).resolves.toEqual({
      kind: 'success',
      reason: 'success',
      message: 'Application deleted successfully.',
      shouldRefresh: true,
    });
  });

  it('returns a refreshable not-found outcome for sequence deletion', async () => {
    const request = vi.fn().mockRejectedValue(new ApiRequestError(404, 'Sequence was not found.'));

    await expect(performDelete('sequence', '/api/applications/app-1/sequences/0000', 'databaseOnly', request)).resolves.toEqual({
      kind: 'error',
      reason: 'not_found',
      message: 'Sequence was not found.',
      shouldRefresh: true,
    });
  });

  it('uses the application fallback message for bare not-found responses', async () => {
    const request = vi.fn().mockRejectedValue(new ApiRequestError(404, 'HTTP Error 404'));

    await expect(performDelete('application', '/api/applications/app-1', 'databaseOnly', request)).resolves.toEqual({
      kind: 'error',
      reason: 'not_found',
      message: 'Application was not found.',
      shouldRefresh: true,
    });
  });

  it('uses the sequence fallback message for bare not-found responses', async () => {
    const request = vi.fn().mockRejectedValue(new ApiRequestError(404, 'HTTP Error 404'));

    await expect(performDelete('sequence', '/api/applications/app-1/sequences/0000', 'databaseOnly', request)).resolves.toEqual({
      kind: 'error',
      reason: 'not_found',
      message: 'Sequence was not found.',
      shouldRefresh: true,
    });
  });

  it('returns a refreshable conflict outcome when protected delete is blocked', async () => {
    const request = vi.fn().mockRejectedValue(new ApiRequestError(409, 'Application cannot be deleted because it still has sequences.'));

    await expect(performDelete('application', '/api/applications/app-1', 'databaseOnly', request)).resolves.toEqual({
      kind: 'error',
      reason: 'conflict',
      message: 'Application cannot be deleted because it still has sequences.',
      shouldRefresh: true,
    });
  });

  it('returns a non-refreshing generic failure message for unexpected errors', async () => {
    const request = vi.fn().mockRejectedValue(new Error('Network unavailable'));

    await expect(performDelete('sequence', '/api/applications/app-1/sequences/0000', 'databaseOnly', request)).resolves.toEqual({
      kind: 'error',
      reason: 'unexpected_error',
      message: 'Failed to delete sequence: Network unavailable',
      shouldRefresh: false,
    });
  });

  it('sends purgeWorkspace delete mode when requested', async () => {
    const request = vi.fn().mockResolvedValue(undefined);

    await performDelete('sequence', '/api/applications/app-1/sequences/0000', 'purgeWorkspace', request);

    expect(request).toHaveBeenCalledWith('/api/applications/app-1/sequences/0000?deleteMode=purgeWorkspace', { method: 'DELETE' });
  });

  it('returns an all-success batch summary', async () => {
    const request = vi.fn().mockResolvedValue(undefined);
    const onProgress = vi.fn();

    const result = await performBatchDelete(
      'sequence',
      'databaseOnly',
      [
        { key: 's-1', label: '0001', url: '/api/applications/app-1/sequences/0001' },
        { key: 's-2', label: '0002', url: '/api/applications/app-1/sequences/0002' },
      ],
      request,
      onProgress,
    );

    expect(result).toEqual({
      entity: 'sequence',
      mode: 'databaseOnly',
      total: 2,
      successCount: 2,
      failureCount: 0,
      results: [
        {
          key: 's-1',
          label: '0001',
          outcome: {
            kind: 'success',
            reason: 'success',
            message: 'Sequence deleted successfully.',
            shouldRefresh: true,
          },
        },
        {
          key: 's-2',
          label: '0002',
          outcome: {
            kind: 'success',
            reason: 'success',
            message: 'Sequence deleted successfully.',
            shouldRefresh: true,
          },
        },
      ],
    });

    expect(onProgress).toHaveBeenNthCalledWith(1, {
      key: 's-1',
      label: '0001',
      outcome: {
        kind: 'success',
        reason: 'success',
        message: 'Sequence deleted successfully.',
        shouldRefresh: true,
      },
    });
    expect(onProgress).toHaveBeenNthCalledWith(2, {
      key: 's-2',
      label: '0002',
      outcome: {
        kind: 'success',
        reason: 'success',
        message: 'Sequence deleted successfully.',
        shouldRefresh: true,
      },
    });
  });

  it('continues batch deletion when one item fails with conflict', async () => {
    const request = vi
      .fn()
      .mockResolvedValueOnce(undefined)
      .mockRejectedValueOnce(new ApiRequestError(409, 'Sequence 0002 is locked.'))
      .mockResolvedValueOnce(undefined);

    const result = await performBatchDelete('sequence', 'databaseOnly', [
      { key: 's-1', label: '0001', url: '/api/applications/app-1/sequences/0001' },
      { key: 's-2', label: '0002', url: '/api/applications/app-1/sequences/0002' },
      { key: 's-3', label: '0003', url: '/api/applications/app-1/sequences/0003' },
    ], request);

    expect(result).toEqual({
      entity: 'sequence',
      mode: 'databaseOnly',
      total: 3,
      successCount: 2,
      failureCount: 1,
      results: [
        {
          key: 's-1',
          label: '0001',
          outcome: {
            kind: 'success',
            reason: 'success',
            message: 'Sequence deleted successfully.',
            shouldRefresh: true,
          },
        },
        {
          key: 's-2',
          label: '0002',
          outcome: {
            kind: 'error',
            reason: 'conflict',
            message: 'Sequence 0002 is locked.',
            shouldRefresh: true,
          },
        },
        {
          key: 's-3',
          label: '0003',
          outcome: {
            kind: 'success',
            reason: 'success',
            message: 'Sequence deleted successfully.',
            shouldRefresh: true,
          },
        },
      ],
    });

    expect(request).toHaveBeenCalledTimes(3);
  });

  it('propagates deleteMode into each batch delete request URL', async () => {
    const request = vi.fn().mockResolvedValue(undefined);

    await performBatchDelete('application', 'purgeWorkspace', [
      { key: 'a-1', label: 'Alpha', url: '/api/applications/app-1' },
      { key: 'a-2', label: 'Beta', url: '/api/applications/app-2?force=true' },
    ], request);

    expect(request).toHaveBeenNthCalledWith(1, '/api/applications/app-1?deleteMode=purgeWorkspace', { method: 'DELETE' });
    expect(request).toHaveBeenNthCalledWith(2, '/api/applications/app-2?force=true&deleteMode=purgeWorkspace', { method: 'DELETE' });
  });

  it('builds application batch delete items from selected application keys', () => {
    expect(buildApplicationBatchDeleteItems(['app-1', 'app-2'])).toEqual([
      { key: 'app-1', label: 'app-1', url: '/api/applications/app-1' },
      { key: 'app-2', label: 'app-2', url: '/api/applications/app-2' },
    ]);
  });

  it('builds sequence batch delete items from selected sequence keys', () => {
    expect(buildSequenceBatchDeleteItems('app-1', ['0001', '0002'])).toEqual([
      { key: '0001', label: '0001', url: '/api/applications/app-1/sequences/0001' },
      { key: '0002', label: '0002', url: '/api/applications/app-1/sequences/0002' },
    ]);
  });

  it('reads batch delete results from optional summaries', () => {
    const summaryResults = [{
      key: 's-1',
      label: '0001',
      outcome: {
        kind: 'success' as const,
        reason: 'success' as const,
        message: 'Sequence deleted successfully.',
        shouldRefresh: true,
      },
    }];

    expect(getBatchDeleteResults({
      entity: 'sequence',
      mode: 'databaseOnly',
      total: 1,
      successCount: 1,
      failureCount: 0,
      results: summaryResults,
    })).toBe(summaryResults);
    expect(getBatchDeleteResults(null)).toEqual([]);
    expect(getBatchDeleteResults(undefined)).toEqual([]);
  });

  it('extracts failed batch delete results from a summary', () => {
    const failedResult = {
      key: 's-2',
      label: '0002',
      outcome: {
        kind: 'error' as const,
        reason: 'conflict' as const,
        message: 'Sequence 0002 is locked.',
        shouldRefresh: true,
      },
    };

    const results = getFailedBatchDeleteResults({
      entity: 'sequence',
      mode: 'databaseOnly',
      total: 2,
      successCount: 1,
      failureCount: 1,
      results: [
        {
          key: 's-1',
          label: '0001',
          outcome: {
            kind: 'success',
            reason: 'success',
            message: 'Sequence deleted successfully.',
            shouldRefresh: true,
          },
        },
        failedResult,
      ],
    });

    expect(results).toEqual([failedResult]);
  });

  it('extracts successful batch delete results from a summary', () => {
    const successfulResult = {
      key: 's-1',
      label: '0001',
      outcome: {
        kind: 'success' as const,
        reason: 'success' as const,
        message: 'Sequence deleted successfully.',
        shouldRefresh: true,
      },
    };

    const results = getSuccessfulBatchDeleteResults({
      entity: 'sequence',
      mode: 'databaseOnly',
      total: 2,
      successCount: 1,
      failureCount: 1,
      results: [
        successfulResult,
        {
          key: 's-2',
          label: '0002',
          outcome: {
            kind: 'error',
            reason: 'conflict',
            message: 'Sequence 0002 is locked.',
            shouldRefresh: true,
          },
        },
      ],
    });

    expect(results).toEqual([successfulResult]);
    expect(getSuccessfulBatchDeleteResults(null)).toEqual([]);
  });
});
