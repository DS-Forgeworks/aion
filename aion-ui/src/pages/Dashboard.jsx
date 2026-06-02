import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useWebSocket } from '../contexts/WebSocketProvider';
import LoadingSkeleton from '../components/LoadingSkeleton';

export default function Dashboard() {
  const navigate = useNavigate();
  const { state: ws, send } = useWebSocket();
  const [health, setHealth] = useState(null);
  const [input, setInput] = useState('');
  const [messages, setMessages] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    const fetchHealth = async () => {
      try {
        const res = await fetch('/api/health');
        if (!res.ok) throw new Error('Health check failed');
        setHealth(await res.json());
        setLoading(false);
      } catch (err) {
        setError(err.message);
        setLoading(false);
      }
    };
    fetchHealth();
    const interval = setInterval(fetchHealth, 10000);
    return () => clearInterval(interval);
  }, []);

  useEffect(() => {
    if (ws.messages.length > 0) {
      setMessages(prev => [...prev, ws.messages[ws.messages.length - 1]].slice(-50));
    }
  }, [ws.messages]);

  const handleSend = () => {
    if (!input.trim()) return;
    send(null, { text: input.trim() });
    setMessages(prev => [...prev, { type: 'outgoing', body: { text: input.trim() } }]);
    setInput('');
  };

  const agentList = Object.values(ws.agents);

  if (loading) return <LoadingSkeleton />;

  if (error) {
    return (
      <div className="page dashboard">
        <div className="error-state">
          <span className="error-icon">⚠️</span>
          <p>Failed to connect: {error}</p>
          <button onClick={() => window.location.reload()}>Retry</button>
        </div>
      </div>
    );
  }

  return (
    <div className="page dashboard">
      <header className="page-header">
        <h1>Dashboard</h1>
        <div className="header-stats">
          {health && (
            <>
              <div className="stat-badge">
                <span className="stat-label">Version</span>
                <span className="stat-value">{health.version}</span>
              </div>
              <div className="stat-badge">
                <span className="stat-label">Agents</span>
                <span className="stat-value">{health.agents ?? agentList.length}</span>
              </div>
              <div className="stat-badge">
                <span className="stat-label">Uptime</span>
                <span className="stat-value">{Math.floor((health.uptime || 0) / 3600)}h</span>
              </div>
              <div className="stat-badge">
                <span className="stat-label">LLM</span>
                <span className="stat-value">{health.llm_status}</span>
              </div>
            </>
          )}
        </div>
      </header>

      <div className="dashboard-grid">
        <section className="card agent-overview">
          <h2>Agents</h2>
          {agentList.length === 0 ? (
            <div className="empty-state small">
              <p>No agents connected</p>
              <p className="hint">Agents appear here when they register via WebSocket</p>
            </div>
          ) : (
            <div className="agent-list">
              {agentList.map((agent) => (
                <div key={agent.agent_id} className="agent-card">
                  <span className={`agent-status ${agent.status}`} />
                  <span className="agent-name">{agent.display_name || agent.agent_id}</span>
                  <span className="agent-id">{agent.agent_id}</span>
                </div>
              ))}
            </div>
          )}
        </section>

        <section className="card send-message">
          <h2>Quick Send</h2>
          <div className="message-list">
            {messages.map((msg, i) => (
              <div key={i} className={`message ${msg.type || 'incoming'}`}>
                <span className="msg-content">
                  {typeof msg.body?.text === 'string' ? msg.body.text : JSON.stringify(msg.body)}
                </span>
              </div>
            ))}
          </div>
          <div className="input-row">
            <input
              type="text"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleSend()}
              placeholder="Type a message..."
            />
            <button onClick={handleSend}>Send</button>
          </div>
        </section>
      </div>
    </div>
  );
}
