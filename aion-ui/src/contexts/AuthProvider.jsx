import { createContext, useContext, useReducer, useEffect } from 'react';

const AuthContext = createContext(null);

const storedUser = (() => {
  try {
    const u = localStorage.getItem('aion_user');
    return u ? JSON.parse(u) : null;
  } catch { return null; }
})();

const initialState = {
  user: storedUser,
  token: localStorage.getItem('aion_token'),
  loading: false,
  error: null,
};

function authReducer(state, action) {
  switch (action.type) {
    case 'LOGIN_START': return { ...state, loading: true, error: null };
    case 'LOGIN_OK': return { ...state, loading: false, user: action.user, token: action.token, error: null };
    case 'LOGIN_FAIL': return { ...state, loading: false, error: action.error, user: null, token: null };
    case 'LOGOUT': return { ...state, user: null, token: null, error: null };
    case 'LOADING': return { ...state, loading: true };
    case 'SET_AUTH': return { ...state, user: action.user, token: action.token, loading: false };
    case 'SET_SESSION': return { ...state, token: action.token };
    default: return state;
  }
}

export function AuthProvider({ children }) {
  const [state, dispatch] = useReducer(authReducer, initialState);

  useEffect(() => {
    if (state.user) {
      localStorage.setItem('aion_user', JSON.stringify(state.user));
    } else {
      localStorage.removeItem('aion_user');
    }
    if (state.token) {
      localStorage.setItem('aion_token', state.token);
    } else {
      localStorage.removeItem('aion_token');
    }
  }, [state.user, state.token]);

  const login = async (provider) => {
    dispatch({ type: 'LOGIN_START' });
    try {
      const res = await fetch(`/api/auth/${provider}`, { method: 'POST' });
      if (!res.ok) throw new Error(`Login failed: ${res.status}`);
      const data = await res.json();
      dispatch({ type: 'LOGIN_OK', user: data.user, token: data.token });
    } catch (err) {
      dispatch({ type: 'LOGIN_FAIL', error: err.message });
    }
  };

  const logout = () => {
    dispatch({ type: 'LOGOUT' });
  };

  return (
    <AuthContext.Provider value={{ ...state, login, logout, dispatch }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be inside AuthProvider');
  return ctx;
}
