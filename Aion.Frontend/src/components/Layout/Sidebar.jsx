import { NavLink } from 'react-router-dom';
import './Sidebar.css';

const NAV_ITEMS = [
  { path: '/', label: 'Dashboard', icon: '📊' },
  { path: '/rooms', label: 'Rooms', icon: '💬' },
  { path: '/logs', label: 'Logs', icon: '📋' },
  { path: '/settings', label: 'Settings', icon: '⚙️' },
];

export default function Sidebar() {
  return (
    <nav className="aion-sidebar">
      <ul className="nav-list">
        {NAV_ITEMS.map(item => (
          <li key={item.path}>
            <NavLink 
              to={item.path} 
              end={item.path === '/'}
              className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}
            >
              <span className="nav-icon">{item.icon}</span>
              <span className="nav-label">{item.label}</span>
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  );
}
