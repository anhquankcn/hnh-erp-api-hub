export const baseUrl = __ENV.BASE_URL || 'http://localhost:8008';
export const authToken = __ENV.AUTH_TOKEN || '';

export function authHeaders(extra = {}) {
  const headers = {
    'Content-Type': 'application/json',
    ...extra,
  };

  if (authToken) {
    headers.Authorization = `Bearer ${authToken}`;
  }

  return headers;
}

export function taggedRequestParams(name, extraHeaders = {}) {
  return {
    headers: authHeaders(extraHeaders),
    tags: { name },
  };
}
