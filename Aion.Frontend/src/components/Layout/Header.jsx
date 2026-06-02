import { useWebSocket } from '../../providers/WebSocketProvider';
import { useAuth } from '../../providers/AuthProvider';
import './Header.css';

const STATUS_LABELS = {
  connected: '🟢 Connected',
  connecting: '🟡 Connecting...',
  disconnected: '🔴 Disconnected',
  error: '⚠️ Error'
};

export default function Header() {
  const { status } = useWebSocket();
  const { user, logout } = useAuth();

  return (
    <header className="aion-header">
      <div className="header-left">
        <h1 className="header-title">AION</h1>
        <span className="header-subtitle">· {user?.display_name || 'Dashboard'}</span>
      </div>
      <div className="header-right">
        <span className={`status-chip status-${status}`}>
          {STATUS_LABELS[status] || '🔴 Disconnected'}
        </span>
        {user && (
          <div className="user-menu">
            <span className="user-avatar">👤</span>
            <span className="user-name">{user.display_name}</span>
            <button className="btn-text" onClick={logout}>Logout</button>
          </div>
        )}
      </div>
    </header>
  );
}
