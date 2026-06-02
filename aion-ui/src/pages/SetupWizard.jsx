import { useState } from 'react';

const STEPS = [
  { id: 'welcome', label: 'Welcome' },
  { id: 'llm', label: 'LLM Setup' },
  { id: 'safety', label: 'Safety' },
  { id: 'mesh', label: 'Mesh' },
  { id: 'confirm', label: 'Confirm' },
];

export default function SetupWizard() {
  const [step, setStep] = useState(0);
  const [config, setConfig] = useState({
    provider: 'ollama',
    model: 'qwen3:8b',
    apiKey: '',
    endpoint: '',
    safeMode: true,
    shellEnabled: false,
    meshEnabled: false,
    meshPort: 6970,
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);
  const [done, setDone] = useState(false);

  const update = (key, value) => {
    setConfig(prev => ({ ...prev, [key]: value }));
  };

  const handleSave = async () => {
    setSaving(true);
    setError(null);
    try {
      const body = {
        llm: {
          provider: config.provider,
          model: config.model,
          apiKey: config.apiKey || null,
          endpoint: config.endpoint || null,
        },
        safety: {
          safeMode: config.safeMode,
          shellEnabled: config.shellEnabled,
        },
        mesh: {
          enabled: config.meshEnabled,
          port: config.meshPort,
        },
      };

      const res = await fetch('/api/setup', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });

      if (!res.ok) throw new Error(`Setup failed: ${res.status}`);
      setDone(true);
    } catch (err) {
      setError(err.message);
    } finally {
      setSaving(false);
    }
  };

  if (done) {
    return (
      <div className="setup-wizard">
        <div className="setup-card">
          <div className="setup-icon">🎉</div>
          <h2>Setup Complete!</h2>
          <p>AION is ready to go. Restart the server to apply changes, or continue configuring.</p>
          <div className="setup-actions">
            <button className="btn-primary" onClick={() => setDone(false)}>
              Back to Setup
            </button>
            <a href="/dashboard" className="btn-secondary">Go to Dashboard</a>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="setup-wizard">
      <div className="setup-card">
        <div className="setup-steps">
          {STEPS.map((s, i) => (
            <div
              key={s.id}
              className={`step-indicator ${i === step ? 'active' : ''} ${i < step ? 'done' : ''}`}
              onClick={() => i < step && setStep(i)}
            >
              <span className="step-number">{i < step ? '✓' : i + 1}</span>
              <span className="step-label">{s.label}</span>
            </div>
          ))}
        </div>

        <div className="setup-content">
          {step === 0 && (
            <>
              <h2>Welcome to AION</h2>
              <p>This wizard will help you set up your Agent Swarm Operating System.</p>
              <p>You'll need:</p>
              <ul>
                <li>An LLM provider (Ollama, OpenAI, or DeepSeek)</li>
                <li>A model to use (default: qwen3:8b)</li>
                <li>Optional: API key for OpenAI/DeepSeek</li>
              </ul>
            </>
          )}

          {step === 1 && (
            <>
              <h2>LLM Configuration</h2>
              <div className="setup-field">
                <label>Provider</label>
                <select value={config.provider} onChange={(e) => update('provider', e.target.value)}>
                  <option value="ollama">Ollama (Local)</option>
                  <option value="openai">OpenAI</option>
                  <option value="deepseek">DeepSeek</option>
                </select>
              </div>
              <div className="setup-field">
                <label>Model</label>
                <input
                  type="text"
                  value={config.model}
                  onChange={(e) => update('model', e.target.value)}
                  placeholder="qwen3:8b"
                />
              </div>
              {config.provider !== 'ollama' && (
                <div className="setup-field">
                  <label>API Key</label>
                  <input
                    type="password"
                    value={config.apiKey}
                    onChange={(e) => update('apiKey', e.target.value)}
                    placeholder="sk-..."
                  />
                </div>
              )}
              <div className="setup-field">
                <label>Endpoint (optional)</label>
                <input
                  type="text"
                  value={config.endpoint}
                  onChange={(e) => update('endpoint', e.target.value)}
                  placeholder="http://127.0.0.1:11434"
                />
              </div>
            </>
          )}

          {step === 2 && (
            <>
              <h2>Safety Settings</h2>
              <div className="setup-field checkbox">
                <label>
                  <input
                    type="checkbox"
                    checked={config.safeMode}
                    onChange={(e) => update('safeMode', e.target.checked)}
                  />
                  Safe Mode
                </label>
                <p className="field-hint">Enable restrictions on dangerous operations</p>
              </div>
              <div className="setup-field checkbox">
                <label>
                  <input
                    type="checkbox"
                    checked={config.shellEnabled}
                    onChange={(e) => update('shellEnabled', e.target.checked)}
                  />
                  Shell Commands
                </label>
                <p className="field-hint">Allow agents to run shell commands</p>
              </div>
            </>
          )}

          {step === 3 && (
            <>
              <h2>Mesh Configuration</h2>
              <div className="setup-field checkbox">
                <label>
                  <input
                    type="checkbox"
                    checked={config.meshEnabled}
                    onChange={(e) => update('meshEnabled', e.target.checked)}
                  />
                  Enable Agent Mesh
                </label>
                <p className="field-hint">Allows multiple agents to communicate via WebSocket</p>
              </div>
              {config.meshEnabled && (
                <div className="setup-field">
                  <label>Mesh Port</label>
                  <input
                    type="number"
                    value={config.meshPort}
                    onChange={(e) => update('meshPort', parseInt(e.target.value) || 6970)}
                  />
                  <p className="field-hint">Default: 6970</p>
                </div>
              )}
            </>
          )}

          {step === 4 && (
            <>
              <h2>Confirm Settings</h2>
              <div className="setup-summary">
                <div className="summary-row"><span>Provider:</span><span>{config.provider}</span></div>
                <div className="summary-row"><span>Model:</span><span>{config.model}</span></div>
                <div className="summary-row"><span>Endpoint:</span><span>{config.endpoint || '(default)'}</span></div>
                <div className="summary-row"><span>Safe Mode:</span><span>{config.safeMode ? 'Yes' : 'No'}</span></div>
                <div className="summary-row"><span>Shell:</span><span>{config.shellEnabled ? 'Enabled' : 'Disabled'}</span></div>
                <div className="summary-row"><span>Mesh:</span><span>{config.meshEnabled ? `Enabled (port ${config.meshPort})` : 'Disabled'}</span></div>
              </div>

              {error && <div className="error-banner">{error}</div>}

              <button
                className="btn-primary btn-large"
                onClick={handleSave}
                disabled={saving}
              >
                {saving ? 'Saving...' : 'Complete Setup'}
              </button>
            </>
          )}
        </div>

        <div className="setup-nav">
          <button
            disabled={step === 0}
            onClick={() => setStep(s => s - 1)}
          >
            Back
          </button>
          <span className="step-counter">{step + 1} / {STEPS.length}</span>
          <button
            disabled={step === STEPS.length - 1}
            onClick={() => setStep(s => s + 1)}
          >
            Next
          </button>
        </div>
      </div>
    </div>
  );
}
