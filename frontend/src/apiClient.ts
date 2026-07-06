type ProblemDetails = {
  type?: string;
  title?: string;
  status?: number;
  traceId?: string;
  errors?: Record<string, string[]>;
  message?: string;
};

export class ApiRequestError extends Error {
  readonly status: number;
  readonly title?: string;
  readonly type?: string;
  readonly traceId?: string;

  constructor(status: number, message: string, details?: Pick<ProblemDetails, 'title' | 'type' | 'traceId'>) {
    super(message);
    this.name = 'ApiRequestError';
    this.status = status;
    this.title = details?.title;
    this.type = details?.type;
    this.traceId = details?.traceId;
  }
}

const buildErrorMessage = (status: number, data?: ProblemDetails) => {
  let errorMsg = `HTTP Error ${status}`;

  if (!data) {
    return errorMsg;
  }

  if (data.message) {
    errorMsg = data.message;
  } else if (data.title) {
    errorMsg = data.title;

    if (data.errors) {
      const details = Object.entries(data.errors)
        .map(([key, vals]) => `${key}: ${vals.join(', ')}`)
        .join(' | ');

      errorMsg += ` - ${details}`;
    }
  }

  return errorMsg;
};

const buildHeaders = (init?: RequestInit) => {
  const headers = new Headers(init?.headers);
  const apiKey = import.meta.env.VITE_API_KEY;

  if (apiKey && !headers.has('X-RA-Tools-Api-Key')) {
    headers.set('X-RA-Tools-Api-Key', apiKey);
  }

  return headers;
};

export const buildJsonRequestInit = (method: string, body: unknown): RequestInit => ({
  method,
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
});

export const apiFetch = async (url: string, options?: RequestInit) => {
  const res = await fetch(url, { ...options, headers: buildHeaders(options) });

  if (!res.ok) {
    let data: ProblemDetails | undefined;

    try {
      data = await res.json();
    } catch {
      data = undefined;
    }

    throw new ApiRequestError(res.status, buildErrorMessage(res.status, data), {
      title: data?.title,
      type: data?.type,
      traceId: data?.traceId,
    });
  }

  if (res.status === 204) {
    return undefined;
  }

  return res.json();
};
