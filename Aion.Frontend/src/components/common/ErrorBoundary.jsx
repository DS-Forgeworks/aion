import { Component } from 'react';

export default class ErrorBoundary extends Component {
  constructor(props) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error) {
    return { hasError: true, error };
  }

  render() {
    if (this.state.hasError) {
      return (
        <div style={{ padding: 40, textAlign: 'center' }}>
          <div style={{ fontSize: 32, marginBottom: 12 }}>⚠️</div>
          <h3 style={{ color: '#e0e0f0', margin: '0 0 8px' }}>
            This section crashed
          </h3>
          <p style={{ color: '#8888aa', fontSize: 13, margin: '0 0 16px' }}>
            The rest of the app is still running.
          </p>
          <button 
            onClick={() => this.setState({ hasError: false, error: null })}
            style={{
              background: 'rgba(124,58,237,0.15)', color: '#c084fc',
              border: '1px solid rgba(124,58,237,0.3)', padding: '8px 20px',
              borderRadius: 6, cursor: 'pointer', fontSize: 13
            }}
          >
            Reload section
          </button>
        </div>
      );
    }
    return this.props.children;
  }
}
