import { afterEach, describe, expect, it, vi } from 'vitest';

import { ApiRequestError } from './apiClient';
import { performDelete } from './deleteActions';

describe('deleteActions', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
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
});
