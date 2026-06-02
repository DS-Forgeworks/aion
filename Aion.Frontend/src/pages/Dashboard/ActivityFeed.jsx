import { useEffect, useRef } from 'react';
import './ActivityFeed.css';

export default function ActivityFeed({ entries = [] }) {
  const bottomRef = useRef(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [entries.length]);

  if (entries.length === 0) {
    return (
      <div className="feed-empty">
        Waiting for activity...
      </div>
    );
  }

  return (
    <div className="activity-feed">
      {entries.map(entry => (
        <div key={entry.id} className={`feed-entry ${entry.severity ? `severity-${entry.severity}` : ''}`}>
          <span className="feed-time">{entry.timestamp || ''}</span>
          <span className="feed-text">{entry.text || entry.message || JSON.stringify(entry.body)}</span>
        </div>
      ))}
      <div ref={bottomRef} />
    </div>
  );
}
