import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { AuthProvider } from './contexts/AuthProvider';
import { WebSocketProvider } from './contexts/WebSocketProvider';
import Layout from './components/Layout';
import ErrorBoundary from './components/ErrorBoundary';
import Dashboard from './pages/Dashboard';
import Room from './pages/Room';
import Logs from './pages/Logs';
import Settings from './pages/Settings';
import SetupWizard from './pages/SetupWizard';

const WS_URL = `ws://${window.location.hostname}:${window.AION_WS_PORT || 6970}/hub/mesh`;

export default function App() {
  return (
    <ErrorBoundary>
      <AuthProvider>
        <WebSocketProvider url={WS_URL}>
          <BrowserRouter>
            <Routes>
              <Route element={<Layout />}>
                <Route path="/" element={<Dashboard />} />
                <Route path="/dashboard" element={<Dashboard />} />
                <Route path="/room/:id" element={<Room />} />
                <Route path="/logs" element={<Logs />} />
                <Route path="/settings" element={<Settings />} />
                <Route path="/setup" element={<SetupWizard />} />
              </Route>
            </Routes>
          </BrowserRouter>
        </WebSocketProvider>
      </AuthProvider>
    </ErrorBoundary>
  );
}
