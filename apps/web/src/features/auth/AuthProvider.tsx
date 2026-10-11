import { useEffect, useState, useSyncExternalStore, type ReactNode } from 'react';
import { request } from '../../api/client';
import { AuthContext } from './context';
import { getSession, getSessionVersion, readSession, setSession, subscribeSession } from './session';

export function AuthProvider({ children }: { children: ReactNode }) {
  const session = useSyncExternalStore(subscribeSession, getSession);
  const [isChecking, setIsChecking] = useState(() => getSession() !== null);

  useEffect(() => {
    const restored = getSession();
    if (!restored) return;
    let active = true;
    void request<unknown>('/auth/me').then((agent) => {
      if (!active || getSession() !== restored) return;
      const refreshed = readSession({ ...restored, agent });
      if (!refreshed) throw new Error('Invalid session response.');
      setSession(refreshed);
    }).catch(() => {
      if (active && getSession() === restored) setSession(null);
    }).finally(() => { if (active) setIsChecking(false); });
    return () => { active = false; };
  }, []);

  useEffect(() => {
    if (!session) return;
    const delay = Date.parse(session.expiresAtUtc) - Date.now();
    const handle = window.setTimeout(() => {
      if (getSession() === session) setSession(null);
    }, Math.max(0, delay));
    return () => window.clearTimeout(handle);
  }, [session]);

  const login = async (email: string, password: string): Promise<boolean> => {
    const version = getSessionVersion();
    const result: unknown = await request<unknown>('/auth/login', {
      method: 'POST', body: JSON.stringify({ email: email.trim(), password }),
    });
    const validated = readSession(result);
    if (!validated) throw new Error('The server returned an invalid login response.');
    if (version !== getSessionVersion()) return false;
    setSession(validated);
    return true;
  };

  return <AuthContext.Provider value={{ session, isChecking, login, logout: () => setSession(null) }}>
    {children}
  </AuthContext.Provider>;
}
