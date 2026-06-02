import { useNavigate } from 'react-router-dom';
import './AgentCard.css';

const STATUS_ICONS = { online: '🟢', processing: '🟡', offline: '🔴' };

export default function AgentCard({ agent }) {
  const nav = useNavigate();
  return (
    <div className="agent-card" onClick={() => nav(`/room/${agent.agent_id}`)}>
      <div className="agent-card-header">
        <span className="agent-status-icon">{STATUS_ICONS[agent.status] || '🔴'}</span>
        <span className="agent-name">{agent.display_name || agent.agent_id}</span>
      </div>
      <div className="agent-card-details">
        <span>Last: {agent.last_seen ? '2 min ago' : 'N/A'}</span>
        {agent.current_task && <span className="agent-task">{agent.current_task}</span>}
      </div>
    </div>
  );
}
