import { useState, useEffect } from 'react';

export default function Settings() {
  const [config, setConfig] = useState(null);
  const [models, setModels] = useState([]);
  const [modelsLoading, setModelsLoading] = useState(true);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [saving, setSaving] = useState(false);
  const [saveMsg, setSaveMsg] = useState(null);

  const fetchModels = async () => {
    setModelsLoading(true);
    try {
      const res = await fetch('/api/models');
      if (res.ok) {
        const data = await res.json();
        setModels(Array.isArray(data) ? data : data.models || []);
      }
    } catch {}
    setModelsLoading(false);
  };

  useEffect(() => {
    const fetchConfig = async () => {
      try {
        const res = await fetch('/api/config');
        if (!res.ok) throw new Error('Failed to load config');
        setConfig(await res.json());
      } catch (err) {
        setError(err.message);
      } finally {
        setLoading(false);
      }
    };
    fetchConfig();
    fetchModels();
  }, []);

  const handleSave = async () => {
    setSaving(true);
    setSaveMsg(null);
    try {
      const res = await fetch('/api/config', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(config),
      });
      if (!res.ok) throw new Error(`Save failed: ${res.status}`);
      setSaveMsg({ type: 'success', text: 'Configuration saved. Restart server to apply.' });
    } catch (err) {
      setSaveMsg({ type: 'error', text: err.message });
    } finally {
      setSaving(false);
    }
  };

  const formatSize = (bytes) => {
    const gb = bytes / 1e9;
    return gb >= 1 ? `${gb.toFixed(1)}GB` : `${(bytes / 1e6).toFixed(0)}MB`;
  };

  if (loading) {
    return (
      <div className="page settings-page">
        <div className="loading-skeleton">
          {[...Array(8)].map((_, i) => <div key={i} className="skeleton-row" />)}
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="page settings-page">
        <div className="error-state">
          <span className="error-icon">⚠️</span>
          <p>Failed to load settings: {error}</p>
          <button onClick={() => window.location.reload()}>Retry</button>
        </div>
      </div>
    );
  }

  return (
    <div className="page settings-page">
      <header className="page-header">
        <h1>Settings</h1>
        <button className="btn-primary" onClick={handleSave} disabled={saving}>
          {saving ? 'Saving...' : 'Save'}
        </button>
      </header>

      {saveMsg && (
        <div className={`msg-banner ${saveMsg.type}`}>
          {saveMsg.text}
        </div>
      )}

      <section className="settings-section">
        <h2>LLM Configuration</h2>
        <div className="settings-grid">
          <div className="setting">
            <label>Provider</label>
            <select
              value={config?.llm?.provider || 'ollama'}
              onChange={(e) => setConfig(prev => ({
                ...prev, llm: { ...prev.llm, provider: e.target.value }
              }))}
            >
              <option value="ollama">Ollama</option>
              <option value="openai">OpenAI</option>
              <option value="deepseek">DeepSeek</option>
            </select>
          </div>
          <div className="setting">
            <label>Model</label>
            <div className="model-select-row">
              <select
                className="model-dropdown"
                value={config?.llm?.model || ''}
                onClick={fetchModels}
                onChange={(e) => setConfig(prev => ({
                  ...prev, llm: { ...prev.llm, model: e.target.value }
                }))}
              >
                {modelsLoading && <option value="">Loading models...</option>}
                {!modelsLoading && models.length === 0 && (
                  <option value="">No models found</option>
                )}
                {models.map((m) => (
                  <option key={m.name} value={m.name}>
                    {m.name} ({formatSize(m.size)})
                  </option>
                ))}
              </select>
              <button
                className="btn-refresh"
                onClick={fetchModels}
                title="Refresh model list — always fetches fresh"
              >
                ↻
              </button>
            </div>
            <p className="setting-hint">Auto-populated from Ollama. Select any model or type a custom name.</p>
          </div>
          <div className="setting">
            <label>Endpoint</label>
            <input
              type="text"
              value={config?.llm?.endpoint || ''}
              onChange={(e) => setConfig(prev => ({
                ...prev, llm: { ...prev.llm, endpoint: e.target.value }
              }))}
              placeholder="http://127.0.0.1:11434"
            />
          </div>
          <div className="setting">
            <label>API Key</label>
            <input
              type="password"
              value={config?.llm?.apiKey || ''}
              onChange={(e) => setConfig(prev => ({
                ...prev, llm: { ...prev.llm, apiKey: e.target.value }
              }))}
              placeholder="sk-..."
            />
          </div>
        </div>
      </section>

      <section className="settings-section">
        <h2>Safety</h2>
        <div className="settings-grid">
          <div className="setting">
            <label>Safe Mode</label>
            <input
              type="checkbox"
              checked={config?.safety?.safeMode !== false}
              onChange={(e) => setConfig(prev => ({
                ...prev, safety: { ...prev.safety, safeMode: e.target.checked }
              }))}
            />
          </div>
          <div className="setting">
            <label>Shell Commands</label>
            <input
              type="checkbox"
              checked={config?.safety?.shellEnabled === true}
              onChange={(e) => setConfig(prev => ({
                ...prev, safety: { ...prev.safety, shellEnabled: e.target.checked }
              }))}
            />
          </div>
        </div>
      </section>
    </div>
  );
}
