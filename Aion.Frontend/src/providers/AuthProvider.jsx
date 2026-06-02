import { createContext, useContext, useState, useCallback } from 'react';

const AuthContext = createContext(null);

export function AuthProvider({ children }) {
  const [apiKey, setApiKey] = useState(() => localStorage.getItem('aion_api_key') || '');
  const [user, setUser] = useState(() => {
    const stored = localStorage.getItem('aion_user');
    return stored ? JSON.parse(stored) : null;
  });

  const login = useCallback((key, userData) => {
    setApiKey(key);
    setUser(userData);
    localStorage.setItem('aion_api_key', key);
    localStorage.setItem('aion_user', JSON.stringify(userData));
  }, []);

  const logout = useCallback(() => {
    setApiKey('');
    setUser(null);
    localStorage.removeItem('aion_api_key');
    localStorage.removeItem('aion_user');
  }, []);

  return (
    <AuthContext.Provider value={{ apiKey, user, login, logout, isAuthenticated: !!apiKey }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
