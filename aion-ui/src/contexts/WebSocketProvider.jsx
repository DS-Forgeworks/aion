import { createContext, useContext, useEffect, useReducer, useRef, useCallback } from 'react';

const WebSocketContext = createContext(null);

const initialState = {
  connected: false,
  agents: {},
  messages: [],
  rooms: ['#general'],
  error: null,
  reconnectAttempt: 0,
};

function wsReducer(state, action) {
  switch (action.type) {
    case 'CONNECTED':
      return { ...state, connected: true, error: null, reconnectAttempt: 0 };
    case 'DISCONNECTED':
      return { ...state, connected: false };
    case 'WELCOME':
      return { ...state, ...action.payload };
    case 'AGENT_STATUS':
      return {
        ...state,
        agents: { ...state.agents, [action.payload.agent_id]: action.payload },
      };
    case 'MESSAGE':
      return { ...state, messages: [...state.messages, action.payload] };
    case 'BROADCAST':
      return { ...state, messages: [...state.messages, action.payload] };
    case 'ERROR':
      return { ...state, error: action.payload };
    default:
      return state;
  }
}

export function WebSocketProvider({ url, children }) {
  const [state, dispatch] = useReducer(wsReducer, initialState);
  const wsRef = useRef(null);
  const reconnectTimeoutRef = useRef(null);

  const connect = useCallback(() => {
    if (wsRef.current?.readyState === WebSocket.OPEN) return;
    if (!url) return;

    try {
      const ws = new WebSocket(url);

      ws.onopen = () => dispatch({ type: 'CONNECTED' });

      ws.onmessage = (event) => {
        try {
          const data = JSON.parse(event.data);
          switch (data.type) {
            case 'welcome':
              dispatch({ type: 'WELCOME', payload: data });
              break;
            case 'agent_status':
              dispatch({ type: 'AGENT_STATUS', payload: data });
              break;
            case 'message':
            case 'broadcast':
            case 'deliver':
              dispatch({ type: 'BROADCAST', payload: data });
              break;
            case 'system':
              console.log('[WS System]', data.body);
              break;
            case 'error':
              dispatch({ type: 'ERROR', payload: data.error });
              break;
          }
        } catch { }
      };

      ws.onclose = () => {
        dispatch({ type: 'DISCONNECTED' });
        const attempt = state.reconnectAttempt + 1;
        const delay = Math.min(1000 * Math.pow(2, attempt), 30000);
        reconnectTimeoutRef.current = setTimeout(connect, delay);
      };

      ws.onerror = () => {
        dispatch({ type: 'ERROR', payload: 'WebSocket connection error' });
      };

      wsRef.current = ws;
    } catch { }
  }, [url]);

  const send = useCallback((to, body, id) => {
    if (wsRef.current?.readyState === WebSocket.OPEN) {
      wsRef.current.send(JSON.stringify({
        type: 'message',
        to: to || null,
        body: body,
        id: id || crypto.randomUUID(),
      }));
    }
  }, []);

  const register = useCallback((reg) => {
    if (wsRef.current?.readyState === WebSocket.OPEN) {
      wsRef.current.send(JSON.stringify({ type: 'register', ...reg }));
    }
  }, []);

  const joinRoom = useCallback((room) => {
    if (wsRef.current?.readyState === WebSocket.OPEN) {
      wsRef.current.send(JSON.stringify({ type: 'join', room }));
    }
  }, []);

  useEffect(() => {
    connect();
    return () => {
      wsRef.current?.close();
      clearTimeout(reconnectTimeoutRef.current);
    };
  }, [connect]);

  const value = { state, send, register, joinRoom, reconnect: connect };

  return (
    <WebSocketContext.Provider value={value}>
      {children}
    </WebSocketContext.Provider>
  );
}

export function useWebSocket() {
  const ctx = useContext(WebSocketContext);
  if (!ctx) throw new Error('useWebSocket must be inside WebSocketProvider');
  return ctx;
}
