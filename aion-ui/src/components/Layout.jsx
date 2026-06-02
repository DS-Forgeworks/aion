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
          <h1 className="sidebar-logo">AION</h1>
          <span className="sidebar-subtitle">Agent Swarm OS</span>
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
