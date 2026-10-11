import type { LoginResponse } from '../../types/api';

const storageKey = 'northwind.auth';
const listeners = new Set<() => void>();

export function readSession(value: unknown): LoginResponse | null {
  if (typeof value !== 'object' || value === null ||
      !('accessToken' in value) || typeof value.accessToken !== 'string' || !value.accessToken ||
      !('expiresAtUtc' in value) || typeof value.expiresAtUtc !== 'string' ||
      !Number.isFinite(Date.parse(value.expiresAtUtc)) || Date.parse(value.expiresAtUtc) <= Date.now() ||
      !('agent' in value) || typeof value.agent !== 'object' || value.agent === null) return null;
  const agent = value.agent;
  if (!('id' in agent) || typeof agent.id !== 'number' || !Number.isSafeInteger(agent.id) || agent.id <= 0 ||
      !('fullName' in agent) || typeof agent.fullName !== 'string' ||
      !('email' in agent) || typeof agent.email !== 'string') return null;
  return { accessToken: value.accessToken, expiresAtUtc: value.expiresAtUtc,
    agent: { id: agent.id, fullName: agent.fullName, email: agent.email } };
}

function restore(): LoginResponse | null {
  try { return readSession(JSON.parse(sessionStorage.getItem(storageKey) ?? 'null')); }
  catch { return null; }
}

let session = restore();
let version = 0;
export const getSession = () => session;
export const getSessionVersion = () => version;
export const subscribeSession = (listener: () => void) => {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
};

export function setSession(value: LoginResponse | null): void {
  session = value;
  version++;
  try {
    if (value) sessionStorage.setItem(storageKey, JSON.stringify(value));
    else sessionStorage.removeItem(storageKey);
  } catch { /* Storage may be unavailable; the in-memory session still works. */ }
  listeners.forEach((listener) => listener());
}
