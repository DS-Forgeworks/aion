import { useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { useWebSocket } from '../contexts/WebSocketProvider';

export default function Room() {
  const { id } = useParams();
  const { state: ws, joinRoom } = useWebSocket();

  useEffect(() => {
    if (id) joinRoom(`#${id}`);
  }, [id, joinRoom]);

  const roomMessages = ws.messages?.filter(m => m.room === `#${id}`) || [];

  return (
    <div className="page room-page">
      <h1>Room: #{id}</h1>
      <div className="room-messages">
        {roomMessages.length === 0 && (
          <div className="empty-state">
            <span className="empty-icon">💬</span>
            <p>No messages in this room yet</p>
          </div>
        )}
        {roomMessages.map((msg, i) => (
          <div key={i} className="message-card">
            <span className="msg-from">{msg.from}</span>
            <span className="msg-body">{typeof msg.body === 'string' ? msg.body : JSON.stringify(msg.body)}</span>
          </div>
        ))}
      </div>
    </div>
  );
}
