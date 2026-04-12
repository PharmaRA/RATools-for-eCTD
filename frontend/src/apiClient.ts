export class ApiRequestError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiRequestError';
    this.status = status;
  }
}

export const apiFetch = async (url: string, options?: RequestInit) => {
  const res = await fetch(url, options);

  if (!res.ok) {
    let errorMsg = `HTTP Error ${res.status}`;

    try {
      const data = await res.json();

      if (data.message) {
        errorMsg = data.message;
      } else if (data.title) {
        errorMsg = data.title;

        if (data.errors) {
          const details = Object.entries(data.errors)
            .map(([key, vals]) => `${key}: ${(vals as string[]).join(', ')}`)
            .join(' | ');

          errorMsg += ` - ${details}`;
        }
      }
    } catch {
      // Leave the fallback message in place when error JSON is unavailable.
    }

    throw new ApiRequestError(res.status, errorMsg);
  }

  if (res.status === 204) {
    return undefined;
  }

  return res.json();
};
