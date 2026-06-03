import { Outlet, NavLink } from 'react-router-dom';
import { useWebSocket } from '../contexts/WebSocketProvider';
import { useAuth } from '../contexts/AuthProvider';

export default function Layout() {
  const { state: ws } = useWebSocket();
  const { user } = useAuth();

  return (
    <div className="app-layout">
      <aside className="sidebar">
        <div className="sidebar-header">
          <svg className="sidebar-logo-icon" viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
            <defs>
              <linearGradient id="lg" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" stop-color="#6c63ff"/>
                <stop offset="100%" stop-color="#8b7aff"/>
              </linearGradient>
            </defs>
            <polygon points="16,2 28,12 16,16 4,12" fill="url(#lg)" opacity="0.9"/>
            <polygon points="4,12 16,16 16,30 4,20" fill="url(#lg)" opacity="0.6"/>
            <polygon points="28,12 16,16 16,30 28,20" fill="url(#lg)" opacity="0.35"/>
            <circle cx="16" cy="16" r="2" fill="white" opacity="0.85"/>
          </svg>
          <span className="sidebar-logo-text">
            A<span className="sidebar-logo-accent">ION</span>
          </span>
        </div>

        <nav className="sidebar-nav">
          <NavLink to="/dashboard" className={({ isActive }) => isActive ? 'nav-link active' : 'nav-link'}>
            <span className="nav-icon">📊</span>
            Dashboard
          </NavLink>
          <NavLink to="/logs" className={({ isActive }) => isActive ? 'nav-link active' : 'nav-link'}>
            <span className="nav-icon">📋</span>
            Logs
          </NavLink>
          <NavLink to="/settings" className={({ isActive }) => isActive ? 'nav-link active' : 'nav-link'}>
            <span className="nav-icon">⚙️</span>
            Settings
          </NavLink>
          <NavLink to="/setup" className={({ isActive }) => isActive ? 'nav-link active' : 'nav-link'}>
            <span className="nav-icon">🔧</span>
            Setup
          </NavLink>
        </nav>

        <div className="sidebar-footer">
          <div className="connection-status">
            <span className={`status-dot ${ws.connected ? 'connected' : 'disconnected'}`} />
            {ws.connected ? 'Connected' : 'Disconnected'}
          </div>
          {user && <div className="user-info">{user.name || user.id}</div>}
        </div>
      </aside>

      <main className="main-content">
        <Outlet />
      </main>
    </div>
  );
}
