import { createContext, useContext } from 'react';
import type { LoginResponse } from '../../types/api';

export interface AuthState {
  session: LoginResponse | null;
  isChecking: boolean;
  login: (email: string, password: string) => Promise<boolean>;
  logout: () => void;
}

export const AuthContext = createContext<AuthState | undefined>(undefined);
export function useAuth(): AuthState {
  const value = useContext(AuthContext);
  if (!value) throw new Error('Authentication provider is missing.');
  return value;
}
