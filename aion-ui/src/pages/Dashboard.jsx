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
  const [models, setModels] = useState([]);
  const [selectedModel, setSelectedModel] = useState('');
  const [configModel, setConfigModel] = useState('');

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

  // Fetch available models — always fresh, no caching
  const fetchModels = async () => {
    try {
      const res = await fetch('/api/models');
      if (res.ok) {
        const data = await res.json();
        setModels(Array.isArray(data) ? data : []);
      }
    } catch {}
  };

  // Get default model from config
  useEffect(() => {
    const fetchConfig = async () => {
      try {
        const res = await fetch('/api/config');
        if (res.ok) {
          const cfg = await res.json();
          const defaultModel = cfg?.llm?.model || '';
          setConfigModel(defaultModel);
          setSelectedModel(defaultModel);
        }
      } catch {}
    };
    fetchConfig();
  }, []);

  useEffect(() => {
    if (ws.messages.length > 0) {
      setMessages(prev => [...prev, ws.messages[ws.messages.length - 1]].slice(-50));
    }
  }, [ws.messages]);

  const handleSend = async () => {
    if (!input.trim()) return;
    setMessages(prev => [...prev, { type: 'outgoing', body: { text: input.trim() }, model: selectedModel }]);
    setInput('');

    try {
      const res = await fetch(`/api/agents/default/message`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: input.trim(), mode: 'chat', model: selectedModel || undefined })
      });
      const data = await res.json();
      if (data.ok) {
        setMessages(prev => [...prev, { type: 'incoming', body: { text: data.reply }, model: selectedModel }]);
      } else {
        setMessages(prev => [...prev, { type: 'incoming', body: { text: `Error: ${data.error || 'unknown'}` } }]);
      }
    } catch (err) {
      setMessages(prev => [...prev, { type: 'incoming', body: { text: `Network error: ${err.message}` } }]);
    }
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
          <div className="model-selector-bar">
            <select
              className="model-dropdown"
              value={selectedModel}
              onClick={fetchModels}
              onChange={(e) => setSelectedModel(e.target.value)}
            >
              {models.length === 0 && <option value={configModel}>{configModel || 'Loading...'}</option>}
              {models.map((m) => (
                <option key={m.name} value={m.name}>
                  {m.name}
                </option>
              ))}
            </select>
            <span className="model-hint">Model shown applies to next message</span>
          </div>
          <div className="message-list">
            {messages.map((msg, i) => (
              <div key={i} className={`message ${msg.type || 'incoming'}`}>
                <span className="msg-model-badge">{msg.model || configModel}</span>
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
