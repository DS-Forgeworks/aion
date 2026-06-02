const BASE = 'http://127.0.0.1:6969';

export function useApi(apiKey) {
  const headers = {
    'Content-Type': 'application/json',
    ...(apiKey ? { 'Authorization': `Bearer ${apiKey}` } : {})
  };

  async function get(path) {
    const res = await fetch(`${BASE}${path}`, { headers });
    if (!res.ok) throw new Error(`GET ${path}: ${res.status}`);
    return res.json();
  }

  async function post(path, body) {
    const res = await fetch(`${BASE}${path}`, {
      method: 'POST',
      headers,
      body: JSON.stringify(body)
    });
    if (!res.ok) throw new Error(`POST ${path}: ${res.status}`);
    return res.json();
  }

  async function put(path, body) {
    const res = await fetch(`${BASE}${path}`, {
      method: 'PUT',
      headers,
      body: JSON.stringify(body)
    });
    if (!res.ok) throw new Error(`PUT ${path}: ${res.status}`);
    return res.json();
  }

  async function del(path) {
    const res = await fetch(`${BASE}${path}`, { method: 'DELETE', headers });
    if (!res.ok) throw new Error(`DELETE ${path}: ${res.status}`);
    return res.json();
  }

  return { get, post, put, del };
}
