import { useState, useEffect } from 'react';

const LEVELS = ['DEBUG', 'INFO', 'WARN', 'ERROR', 'CRIT'];
const LEVEL_COLORS = {
  DEBUG: '#666',
  INFO: '#4fc3f7',
  WARN: '#ffa726',
  ERROR: '#ef5350',
  CRIT: '#d32f2f',
};

export default function Logs() {
  const [logs, setLogs] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [filter, setFilter] = useState('all');
  const [autoRefresh, setAutoRefresh] = useState(true);

  const fetchLogs = async (level) => {
    try {
      setLoading(true);
      const params = level && level !== 'all' ? `?level=${level}` : '';
      const res = await fetch(`/api/logs${params}&limit=100`);
      if (!res.ok) throw new Error('Failed to fetch logs');
      const data = await res.json();
      setLogs(data);
      setError(null);
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchLogs(filter);
  }, [filter]);

  useEffect(() => {
    if (!autoRefresh) return;
    const interval = setInterval(() => fetchLogs(filter), 5000);
    return () => clearInterval(interval);
  }, [filter, autoRefresh]);

  return (
    <div className="page logs-page">
      <header className="page-header">
        <h1>Logs</h1>
        <div className="log-controls">
          <select value={filter} onChange={(e) => setFilter(e.target.value)}>
            <option value="all">All Levels</option>
            {LEVELS.map((l) => (
              <option key={l} value={l}>{l}</option>
            ))}
          </select>
          <button onClick={() => fetchLogs(filter)} disabled={loading}>
            {loading ? 'Loading...' : 'Refresh'}
          </button>
          <label className="auto-refresh-toggle">
            <input
              type="checkbox"
              checked={autoRefresh}
              onChange={(e) => setAutoRefresh(e.target.checked)}
            />
            Auto-refresh
          </label>
        </div>
      </header>

      {error && <div className="error-banner">Error: {error}</div>}

      {logs.length === 0 && !loading && (
        <div className="empty-state">
          <span className="empty-icon">📋</span>
          <p>No log entries found{filters !== 'all' ? ` for level ${filter}` : ''}.</p>
        </div>
      )}

      {loading && logs.length === 0 && (
        <div className="loading-skeleton">
          {[...Array(5)].map((_, i) => (
            <div key={i} className="skeleton-row" />
          ))}
        </div>
      )}

      <div className="log-table">
        {logs.map((log, i) => (
          <div key={i} className={`log-row level-${log.level?.toLowerCase()}`}>
            <span className="log-time">{log.timestamp?.slice(11, 19)}</span>
            <span className="log-level" style={{ color: LEVEL_COLORS[log.level] || '#888' }}>
              [{log.level}]
            </span>
            <span className="log-source">{log.source}</span>
            <span className="log-message">{log.message}</span>
          </div>
        ))}
      </div>
    </div>
  );
}
