// Auth fetch helper — adds Bearer token from localStorage, redirects on 401
export async function authedFetch(url, options = {}) {
  const token = localStorage.getItem('aion_token');
  const headers = { ...options.headers };
  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }
  const res = await fetch(url, { ...options, headers });
  if (res.status === 401) {
    // Token expired or invalid — redirect to login
    localStorage.removeItem('aion_token');
    document.cookie = 'aion_token=; path=/; max-age=0';
    window.location.href = '/login';
    throw new Error('Authentication required');
  }
  return res;
}

// For GET requests
export const authGet = (url) => authedFetch(url);

// For POST/PUT with JSON body
export const authPost = (url, body) =>
  authedFetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
