import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { Spinner } from '../../components/Spinner';
import { useAuth } from './context';

export function ProtectedRoute() {
  const { session, isChecking } = useAuth();
  const location = useLocation();
  if (isChecking) return <Spinner label="Checking session" />;
  return session ? <Outlet /> : <Navigate to="/login" replace
    state={{ from: location.pathname + location.search + location.hash }} />;
}
