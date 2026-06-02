import { useParams } from 'react-router-dom';
import { useState, useEffect, useRef } from 'react';
import { useWebSocket } from '../../providers/WebSocketProvider';
import { useAuth } from '../../providers/AuthProvider';
import { useApi } from '../../hooks/useApi';
import EmptyState from '../../components/common/EmptyState';
import LoadingSkeleton from '../../components/common/LoadingSkeleton';
import './Room.css';

export default function Room() {
  const { id } = useParams();
  const { apiKey } = useAuth();
  const api = useApi(apiKey);
  const { agents, subscribe } = useWebSocket();
  const [messages, setMessages] = useState([]);
  const [input, setInput] = useState('');
  const [mode, setMode] = useState('chat');
  const [loading, setLoading] = useState(true);
  const bottomRef = useRef(null);

  const agent = agents.find(a => a.agent_id === id);
  const isOffline = agent?.status === 'offline';

  useEffect(() => {
    api.get(`/api/agents/${id}/messages?limit=50`)
      .then(data => setMessages(Array.isArray(data) ? data : []))
      .catch(() => setMessages([]))
      .finally(() => setLoading(false));
  }, [id]);

  useEffect(() => {
    const unsub = subscribe('deliver', (msg) => {
      if (msg.to === id || msg.from === id) {
        setMessages(prev => [...prev, { ...msg, incoming: true }]);
      }
    });
    return unsub;
  }, [id, subscribe]);

  useEffect(() => { bottomRef.current?.scrollIntoView({ behavior: 'smooth' }); }, [messages.length]);

  const sendMessage = async () => {
    if (!input.trim()) return;
    const userMsg = { id: crypto.randomUUID(), text: input, from: 'user', timestamp: new Date().toISOString() };
    setMessages(prev => [...prev, userMsg]);
    setInput('');
    try {
      const endpoint = mode === 'task' ? 'task' : 'message';
      await api.post(`/api/agents/${id}/${endpoint}`, { text: input });
    } catch (e) {
      setMessages(prev => [...prev, { id: crypto.randomUUID(), text: `⚠️ Failed to send: ${e.message}`, from: 'system' }]);
    }
  };

  if (loading) return <LoadingSkeleton rows={10} />;

  return (
    <div className="room">
      <div className="room-header">
        <span>{agent?.display_name || id}</span>
        <span className={`room-status ${isOffline ? 'offline' : 'online'}`}>
          {isOffline ? '🔴 Offline' : '🟢 Online'}
        </span>
      </div>

      {isOffline && (
        <div className="room-banner">
          Agent is offline. Messages will be delivered when they reconnect.
        </div>
      )}

      <div className="room-messages">
        {messages.length === 0 ? (
          <EmptyState icon="💬" title="No messages yet" description="Say hello or assign a task." />
        ) : (
          messages.map(msg => (
            <div key={msg.id} className={`msg-bubble msg-${msg.from === 'user' ? 'user' : msg.incoming ? 'agent' : 'agent'}`}>
              <div className="msg-sender">{msg.from === 'user' ? 'You' : msg.from || agent?.display_name || id}</div>
              <div className="msg-text">{msg.text || msg.body?.answer || JSON.stringify(msg.body)}</div>
              <div className="msg-time">{new Date(msg.timestamp || Date.now()).toLocaleTimeString()}</div>
            </div>
          ))
        )}
        <div ref={bottomRef} />
      </div>

      <div className="room-input">
        <button className={`mode-toggle ${mode}`} onClick={() => setMode(mode === 'chat' ? 'task' : 'chat')}>
          [{mode === 'chat' ? 'Chat' : 'Task'}]
        </button>
        <input
          className="input-field"
          value={input}
          onChange={e => setInput(e.target.value)}
          onKeyDown={e => e.key === 'Enter' && sendMessage()}
          placeholder={isOffline ? 'Message (queued for delivery)...' : 'Type a message or paste a lead...'}
          disabled={false}
        />
        <button className="btn-primary btn-send" onClick={sendMessage}>Send</button>
      </div>
    </div>
  );
}
