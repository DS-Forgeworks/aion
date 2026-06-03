import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useWebSocket } from '../contexts/WebSocketProvider';
import { useAuth } from '../contexts/AuthProvider';
import { authedFetch } from '../lib/authFetch';
import LoadingSkeleton from '../components/LoadingSkeleton';

export default function Dashboard() {
  const navigate = useNavigate();
  const { state: ws, send } = useWebSocket();
  const { token, isAuthenticated, logout, firstRun } = useAuth();
  const [health, setHealth] = useState(null);
  const [input, setInput] = useState('');
  const [messages, setMessages] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [models, setModels] = useState([]);
  const [selectedModel, setSelectedModel] = useState('');
  const [configModel, setConfigModel] = useState('');
  const [conversations, setConversations] = useState([]);
  const [currentConvId, setCurrentConvId] = useState(null);
  const [showHistory, setShowHistory] = useState(false);
  const [sending, setSending] = useState(false);
  const [editingMsgId, setEditingMsgId] = useState(null);
  const [editText, setEditText] = useState('');

  // Redirect to login if not authenticated
  if (!token) {
    navigate('/login');
    return null;
  }

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

  const fetchModels = async () => {
    try {
      const res = await authedFetch('/api/models');
      if (res.ok) {
        const data = await res.json();
        setModels(Array.isArray(data) ? data : []);
      }
    } catch {}
  };

  const fetchConversations = async () => {
    try {
      const res = await authedFetch('/api/conversations');
      if (res.ok) {
        const data = await res.json();
        if (data.conversations) setConversations(data.conversations);
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
    fetchConversations();
  }, []);

  useEffect(() => {
    if (ws.messages.length > 0) {
      setMessages(prev => [...prev, ws.messages[ws.messages.length - 1]].slice(-50));
    }
  }, [ws.messages]);

  const loadConversation = async (convId) => {
    try {
      const res = await authedFetch(`/api/conversations/${convId}/messages`);
      if (res.ok) {
        const data = await res.json();
        if (data.messages) {
          setMessages(data.messages.map(m => ({
            type: m.role === 'user' ? 'outgoing' : 'incoming',
            body: { text: m.content },
            model: m.model,
            id: m.id,
            edited: m.edited
          })));
          setCurrentConvId(convId);
        }
      }
    } catch {}
  };

  const handleSend = async () => {
    if (!input.trim() || sending) return;
    setSending(true);

    const userMsg = input.trim();
    setMessages(prev => [...prev, { type: 'outgoing', body: { text: userMsg }, model: selectedModel }]);
    setInput('');

    try {
      const res = await authedFetch(`/api/agents/default/message`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          text: userMsg,
          mode: 'chat',
          model: selectedModel || undefined,
          conversation_id: currentConvId || undefined
        })
      });
      const data = await res.json();
      if (data.reply) {
        setMessages(prev => [...prev, { type: 'incoming', body: { text: data.reply }, model: selectedModel }]);
        // Update current conversation ID from response
        if (data.conversation_id && data.conversation_id !== currentConvId) {
          setCurrentConvId(data.conversation_id);
        }
      } else {
        setMessages(prev => [...prev, { type: 'incoming', body: { text: `Could you clarify what you mean? I'm not sure I understood.` } }]);
      }
    } catch (err) {
      setMessages(prev => [...prev, { type: 'incoming', body: { text: `Network error: ${err.message}` } }]);
    } finally {
      setSending(false);
      fetchConversations(); // refresh sidebar
    }
  };

  const handleFileUpload = async (e) => {
    const file = e.target.files?.[0];
    if (!file) return;

    const formData = new FormData();
    formData.append('uploadedFile', file);

    setSending(true);
    setMessages(prev => [...prev, { type: 'outgoing', body: { text: `📎 Uploading ${file.name}...` } }]);

    try {
      const res = await authedFetch('/api/upload', {
        method: 'POST',
        body: formData,
      });
      const data = await res.json();
      if (data.ok) {
        setMessages(prev => prev.map((m, i) =>
          i === prev.length - 1
            ? { type: 'outgoing', body: { text: `📎 Uploaded: ${data.name} (${Math.round(data.size / 1024)}KB)` } }
            : m
        ));
        // Send the file preview to the agent
        const response = await authedFetch('/api/agents/default/message', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            text: `I've uploaded a file "${data.name}". Here's its content:\n\n${data.preview || 'Binary file uploaded.'}\n\nAnalyze this.`,
            mode: 'chat',
            model: selectedModel || undefined,
            conversation_id: currentConvId || undefined
          })
        });
        const reply = await response.json();
        if (reply.reply) {
          setMessages(prev => [...prev, { type: 'incoming', body: { text: reply.reply }, model: selectedModel }]);
        }
      } else {
        setMessages(prev => [...prev, { type: 'incoming', body: { text: `Upload failed: ${data.error}` } }]);
      }
    } catch (err) {
      setMessages(prev => [...prev, { type: 'incoming', body: { text: `Upload error: ${err.message}` } }]);
    } finally {
      setSending(false);
      e.target.value = '';
    }
  };

  const handleEdit = async (msgId, newContent) => {
    if (!newContent.trim()) return;
    try {
      await authedFetch(`/api/messages/${msgId}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ content: newContent })
      });
      setMessages(prev => prev.map(m =>
        m.id === msgId ? { ...m, body: { ...m.body, text: newContent }, edited: true } : m
      ));
      setEditingMsgId(null);
      setEditText('');
    } catch {}
  };

  const handleRetry = (msgIdx) => {
    // Find the last user message before this assistant message
    for (let i = msgIdx - 1; i >= 0; i--) {
      if (messages[i].type === 'outgoing') {
        // Remove the failed reply and this one, resend the user message
        const lastUserMsg = messages[i].body.text;
        setMessages(prev => prev.slice(0, msgIdx));
        setInput(lastUserMsg);
        break;
      }
    }
  };

  const newConversation = () => {
    setMessages([]);
    setCurrentConvId(null);
  };

  const deleteConversation = async (convId, e) => {
    e.stopPropagation();
    try {
      await authedFetch(`/api/conversations/${convId}`, { method: 'DELETE' });
      if (currentConvId === convId) {
        setMessages([]);
        setCurrentConvId(null);
      }
      fetchConversations();
    } catch {}
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

  const formatTime = (ts) => {
    const d = new Date(ts);
    return d.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
  };

  return (
    <div className="page dashboard">
      <header className="page-header">
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
        <button className="btn-logout" onClick={logout} title="Sign Out">
          Sign Out
        </button>
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
          <div className="chat-header">
            <h2>Chat</h2>
            <div className="chat-header-actions">
              <button className="btn-chat-history" onClick={() => { fetchConversations(); setShowHistory(!showHistory); }} title="Conversation history">
                ☰
              </button>
              <button className="btn-chat-new" onClick={newConversation} title="New conversation">+</button>
            </div>
          </div>

          {/* Slide-out conversation history panel */}
          {showHistory && (
            <div className="conv-history-panel">
              <div className="conv-history-header">
                <span>Conversations</span>
                <button className="btn-close-panel" onClick={() => setShowHistory(false)}>✕</button>
              </div>
              <div className="conv-list">
                {conversations.length === 0 && (
                  <div className="conv-empty">No conversations yet</div>
                )}
                {conversations.map(conv => (
                  <div
                    key={conv.id}
                    className={`conv-item ${conv.id === currentConvId ? 'active' : ''}`}
                    onClick={() => { loadConversation(conv.id); setShowHistory(false); }}
                  >
                    <div className="conv-title">{conv.title}</div>
                    <div className="conv-meta">
                      {conv.messageCount} msgs · {formatTime(conv.updatedAt)}
                    </div>
                    <button className="conv-delete" onClick={(e) => deleteConversation(conv.id, e)} title="Delete">🗑</button>
                  </div>
                ))}
              </div>
            </div>
          )}

          <div className="model-selector-bar">
            <select
              className="model-dropdown"
              value={selectedModel}
              onClick={fetchModels}
              onChange={(e) => setSelectedModel(e.target.value)}
            >
              {models.length === 0 && <option value={configModel}>{configModel || 'Select a model...'}</option>}
              {models.map((m) => (
                <option key={m.name} value={m.name}>
                  {m.name}
                </option>
              ))}
            </select>
            <span className="model-hint">{currentConvId ? 'Continuing conversation' : 'New conversation'}</span>
          </div>

          <div className="message-list">
            {messages.length === 0 && (
              <div className="empty-state small">
                <p>Start a conversation</p>
                <p className="hint">Type a message below to begin</p>
              </div>
            )}
            {messages.map((msg, i) => (
              <div key={i} className={`message ${msg.type || 'incoming'}`}>
                {editingMsgId === msg.id ? (
                  <div className="msg-edit-row">
                    <input
                      type="text"
                      value={editText}
                      onChange={(e) => setEditText(e.target.value)}
                      onKeyDown={(e) => e.key === 'Enter' && handleEdit(msg.id, editText)}
                      autoFocus
                    />
                    <button onClick={() => handleEdit(msg.id, editText)}>Save</button>
                    <button onClick={() => setEditingMsgId(null)}>Cancel</button>
                  </div>
                ) : (
                  <>
                    <span className="msg-model-badge">{msg.model || configModel}</span>
                    <span className="msg-content">
                      {typeof msg.body?.text === 'string' ? msg.body.text : JSON.stringify(msg.body)}
                    </span>
                    <div className="msg-actions">
                      {msg.type === 'outgoing' && (
                        <button className="msg-btn" onClick={() => { setEditingMsgId(msg.id); setEditText(msg.body.text); }} title="Edit">✏️</button>
                      )}
                      {msg.type === 'incoming' && (
                        <button className="msg-btn" onClick={() => handleRetry(i)} title="Retry">🔄</button>
                      )}
                    </div>
                  </>
                )}
              </div>
            ))}
            {sending && <div className="message incoming"><span className="msg-content">Thinking...</span></div>}
          </div>

          <div className="input-row">
            <label className="upload-btn" title="Upload a file">
              <input
                type="file"
                onChange={handleFileUpload}
                disabled={sending}
                style={{ display: 'none' }}
              />
              📎
            </label>
            <input
              type="text"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleSend()}
              placeholder="Type a message..."
              disabled={sending}
            />
            <button onClick={handleSend} disabled={sending}>
              {sending ? '...' : 'Send'}
            </button>
          </div>
        </section>
      </div>
    </div>
  );
}
