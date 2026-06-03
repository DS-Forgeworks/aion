import { createContext, useContext, useReducer, useEffect, useCallback, useState } from 'react';

const AuthContext = createContext(null);

const storedToken = (() => {
  try { return localStorage.getItem('aion_token'); } catch { return null; }
})();

const initialState = {
  user: null,
  token: storedToken,
  loading: !storedToken,  // if no token, not loading
  error: null,
  isAuthenticated: false,
};

function authReducer(state, action) {
  switch (action.type) {
    case 'SET_AUTH': return { ...state, user: action.user, token: action.token, loading: false, isAuthenticated: true, error: null };
    case 'LOGOUT': return { ...state, user: null, token: null, loading: false, isAuthenticated: false, error: null };
    case 'LOADING': return { ...state, loading: true };
    case 'ERROR': return { ...state, error: action.error, loading: false };
    default: return state;
  }
}

export function AuthProvider({ children }) {
  const [state, dispatch] = useReducer(authReducer, initialState);
  const [firstRun, setFirstRun] = useState(false);

  // Check server health and auth status on mount
  useEffect(() => {
    const init = async () => {
      try {
        const res = await fetch('/api/health');
        const data = await res.json();
        setFirstRun(data.first_run);
      } catch {}
    };
    init();
  }, []);

  // Auto-login if token stored
  useEffect(() => {
    if (state.token) {
      localStorage.setItem('aion_token', state.token);
      document.cookie = `aion_token=${state.token}; path=/; max-age=2592000; SameSite=Strict`;
      dispatch({ type: 'SET_AUTH', user: { email: 'admin' }, token: state.token });
    }
  }, []);

  const login = async (username, password) => {
    dispatch({ type: 'LOADING' });
    try {
      const res = await fetch('/api/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password }),
      });
      const data = await res.json();
      if (!data.ok) {
        dispatch({ type: 'ERROR', error: data.error || 'Login failed' });
        return false;
      }
      localStorage.setItem('aion_token', data.token);
      document.cookie = `aion_token=${data.token}; path=/; max-age=2592000; SameSite=Strict`;
      dispatch({ type: 'SET_AUTH', user: { email: username }, token: data.token });
      return true;
    } catch (err) {
      dispatch({ type: 'ERROR', error: 'Connection failed' });
      return false;
    }
  };

  const logout = async () => {
    try {
      fetch('/api/logout', {
        method: 'POST',
        headers: { 'Authorization': `Bearer ${state.token}` }
      });
    } catch {}
    localStorage.removeItem('aion_token');
    document.cookie = 'aion_token=; path=/; max-age=0';
    dispatch({ type: 'LOGOUT' });
    window.location.href = '/login';
  };

  return (
    <AuthContext.Provider value={{ ...state, login, logout, firstRun, dispatch }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be inside AuthProvider');
  return ctx;
}
