import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { AuthProvider, useAuth } from './providers/AuthProvider';
import { WebSocketProvider } from './providers/WebSocketProvider';
import Header from './components/Layout/Header';
import Sidebar from './components/Layout/Sidebar';
import ErrorBoundary from './components/common/ErrorBoundary';
import Dashboard from './pages/Dashboard/Dashboard';
import Room from './pages/Room/Room';
import './App.css';

function AppLayout() {
  const { apiKey } = useAuth();
  return (
    <WebSocketProvider apiKey={apiKey}>
      <div className="app">
        <Header />
        <div className="app-body">
          <Sidebar />
          <main className="app-main">
            <ErrorBoundary>
              <Routes>
                <Route path="/" element={<Dashboard />} />
                <Route path="/room/:id" element={<Room />} />
              </Routes>
            </ErrorBoundary>
          </main>
        </div>
      </div>
    </WebSocketProvider>
  );
}

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <AppLayout />
      </AuthProvider>
    </BrowserRouter>
  );
}
