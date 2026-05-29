import { afterEach, describe, expect, it, vi } from 'vitest';

import { ApiRequestError, apiFetch } from './apiClient';

describe('apiClient', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns parsed JSON when the response succeeds', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: vi.fn().mockResolvedValue({ id: 'app-1' }),
    }));

    await expect(apiFetch('/api/applications')).resolves.toEqual({ id: 'app-1' });
  });

  it('resolves cleanly when the response succeeds with no body', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      status: 204,
      json: vi.fn().mockRejectedValue(new Error('No body')),
    }));

    await expect(apiFetch('/api/applications/app-1', { method: 'DELETE' })).resolves.toBeUndefined();
  });

  it('throws ApiRequestError with backend message and status', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: false,
      status: 409,
      json: vi.fn().mockResolvedValue({ message: 'Application cannot be deleted because it still has sequences.' }),
    }));

    await expect(apiFetch('/api/applications/app-1', { method: 'DELETE' })).rejects.toMatchObject({
      name: 'ApiRequestError',
      status: 409,
      message: 'Application cannot be deleted because it still has sequences.',
    } satisfies Partial<ApiRequestError>);
  });

  it('keeps validation details while preserving the HTTP status', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: false,
      status: 400,
      json: vi.fn().mockResolvedValue({
        title: 'One or more validation errors occurred.',
        errors: {
          CtdSection: [
            'The CtdSection field is required.',
          ],
        },
      }),
    }));

    await expect(apiFetch('/api/applications/app-1/sequences/0000/documents/upload', { method: 'POST' })).rejects.toMatchObject({
      status: 400,
      message: 'One or more validation errors occurred. - CtdSection: The CtdSection field is required.',
    } satisfies Partial<ApiRequestError>);
  });

  it('throws ApiRequestError with ProblemDetails metadata', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: false,
      status: 500,
      headers: new Headers({ 'content-type': 'application/problem+json' }),
      json: vi.fn().mockResolvedValue({
        type: 'https://tools.ietf.org/html/rfc7231#section-6.6.1',
        title: 'An error occurred while processing your request.',
        status: 500,
        traceId: 'trace-123',
      }),
    }));

    await expect(apiFetch('/api/applications')).rejects.toMatchObject({
      name: 'ApiRequestError',
      status: 500,
      message: 'An error occurred while processing your request.',
      title: 'An error occurred while processing your request.',
      type: 'https://tools.ietf.org/html/rfc7231#section-6.6.1',
      traceId: 'trace-123',
    } satisfies Partial<ApiRequestError>);
  });
});
