import { useWebSocket } from '../../providers/WebSocketProvider';
import AgentCard from './AgentCard';
import ActivityFeed from './ActivityFeed';
import EmptyState from '../../components/common/EmptyState';
import LoadingSkeleton from '../../components/common/LoadingSkeleton';
import { useApi } from '../../hooks/useApi';
import { useAuth } from '../../providers/AuthProvider';
import { useState, useEffect } from 'react';
import './Dashboard.css';

export default function Dashboard() {
  const { agents, activityFeed, status } = useWebSocket();
  const { apiKey } = useAuth();
  const api = useApi(apiKey);
  const [health, setHealth] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api.get('/api/health')
      .then(setHealth)
      .catch(() => {})
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <LoadingSkeleton rows={8} />;

  const hasNoAgents = agents.length === 0 && status === 'connected';

  return (
    <div className="dashboard">
      <div className="dashboard-agents">
        <div className="section-header">
          <h2>Agents ({agents.length})</h2>
          <button className="btn-secondary">+ Add</button>
        </div>
        <div className="agent-grid">
          {hasNoAgents ? (
            <EmptyState 
              icon="👋" 
              title="Welcome to AION"
              description="Create your first agent to start automating tasks."
              action={<button className="btn-primary">Create your first agent</button>}
            />
          ) : (
            agents.map(a => <AgentCard key={a.agent_id} agent={a} />)
          )}
        </div>
      </div>
      <div className="dashboard-feed">
        <div className="section-header">
          <h2>Activity Feed</h2>
          <button className="btn-text">Pause Live</button>
        </div>
        <ActivityFeed entries={activityFeed} />
      </div>
      {health && (
        <div className="dashboard-stats">
          <span>{health.agents} agents</span>
          <span>·</span>
          <span>{health.errors_1h || 0} errors / 1h</span>
          <span>·</span>
          <span>Mesh: {health.llm_status || 'OK'}</span>
        </div>
      )}
    </div>
  );
}
