import { createContext, useContext, useEffect, useRef, useState, useCallback } from 'react';

const WebSocketContext = createContext(null);

export function WebSocketProvider({ children, apiKey, url = 'ws://127.0.0.1:6970/hub/dashboard' }) {
  const [status, setStatus] = useState('disconnected');
  const [agents, setAgents] = useState([]);
  const [activityFeed, setActivityFeed] = useState([]);
  const wsRef = useRef(null);
  const backoffRef = useRef(1000);
  const handlersRef = useRef({});

  const connect = useCallback(() => {
    if (wsRef.current?.readyState === WebSocket.OPEN) return;
    setStatus('connecting');
    
    const ws = new WebSocket(url);
    wsRef.current = ws;

    ws.onopen = () => {
      setStatus('connected');
      backoffRef.current = 1000;
      ws.send(JSON.stringify({
        type: 'register',
        agent_id: 'dashboard',
        display_name: 'Dashboard UI',
        status: 'online',
        rooms: ['#general', '#system']
      }));
    };

    ws.onmessage = (event) => {
      try {
        const msg = JSON.parse(event.data);
        
        if (msg.type === 'ping') {
          ws.send(JSON.stringify({ type: 'pong' }));
          return;
        }
        
        if (msg.type === 'welcome') {
          if (msg.missed_messages?.length) {
            setActivityFeed(prev => [
              ...msg.missed_messages.map(m => ({ ...m, id: crypto.randomUUID() })),
              ...prev
            ]);
          }
          return;
        }

        if (msg.type === 'agent_status') {
          setAgents(prev => {
            const existing = prev.findIndex(a => a.agent_id === msg.agent_id);
            if (existing >= 0) {
              const updated = [...prev];
              updated[existing] = { ...updated[existing], ...msg };
              return updated;
            }
            return [...prev, msg];
          });
          return;
        }

        if (msg.type === 'activity') {
          setActivityFeed(prev => [{ ...msg, id: crypto.randomUUID(), timestamp: new Date().toLocaleTimeString() }, ...prev].slice(0, 100));
          return;
        }

        if (msg.type === 'system') {
          setActivityFeed(prev => [{ 
            id: crypto.randomUUID(), 
            text: msg.message, 
            severity: msg.severity || 'info',
            timestamp: new Date().toLocaleTimeString() 
          }, ...prev].slice(0, 100));
          return;
        }

        // Custom handlers
        const handler = handlersRef.current[msg.type];
        if (handler) handler(msg);
        
      } catch (e) {
        console.warn('WS parse error:', e);
      }
    };

    ws.onclose = () => {
      setStatus('disconnected');
      const delay = Math.min(backoffRef.current, 30000);
      setTimeout(connect, delay);
      backoffRef.current *= 2;
    };

    ws.onerror = () => ws.close();
  }, [url]);

  useEffect(() => {
    if (apiKey) connect();
    return () => wsRef.current?.close();
  }, [apiKey, connect]);

  const sendMessage = useCallback((msg) => {
    if (wsRef.current?.readyState === WebSocket.OPEN) {
      wsRef.current.send(JSON.stringify(msg));
    }
  }, []);

  const subscribe = useCallback((type, handler) => {
    handlersRef.current[type] = handler;
    return () => { delete handlersRef.current[type]; };
  }, []);

  return (
    <WebSocketContext.Provider value={{ status, agents, activityFeed, sendMessage, subscribe }}>
      {children}
    </WebSocketContext.Provider>
  );
}

export function useWebSocket() {
  const ctx = useContext(WebSocketContext);
  if (!ctx) throw new Error('useWebSocket must be used within WebSocketProvider');
  return ctx;
}
