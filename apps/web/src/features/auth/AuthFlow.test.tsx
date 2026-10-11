import { act, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../App';
import { request } from '../../api/client';
import type { LoginResponse } from '../../types/api';
import { AuthProvider } from './AuthProvider';
import { LoginPage } from './LoginPage';
import { ProtectedRoute } from './ProtectedRoute';
import { getSession, readSession, setSession } from './session';

const fetchMock = vi.fn<typeof fetch>();
const loginResponse = (): LoginResponse => ({ accessToken: 'test-token',
  expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
  agent: { id: 1, fullName: 'Alex Turner', email: 'alex@example.test' } });

async function mount(path = '/tickets/12') {
  await act(async () => {
    render(<AuthProvider><MemoryRouter initialEntries={[path]}><Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<ProtectedRoute />}><Route element={<App />}>
        <Route path="/tickets/12" element={<h1>Requested ticket</h1>} />
        <Route path="/tickets" element={<h1>Ticket list</h1>} />
      </Route></Route>
    </Routes></MemoryRouter></AuthProvider>);
  });
}

async function submit(email = 'alex@example.test', password = 'Test password') {
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: email } });
  fireEvent.change(screen.getByLabelText('Password'), { target: { value: password } });
  await act(async () => { fireEvent.submit(screen.getByRole('form', { name: 'Sign in' })); });
}

describe('Authentication flow', () => {
  beforeEach(() => {
    setSession(null);
    fetchMock.mockImplementation(async (url) => new Response(JSON.stringify(
      String(url).endsWith('/login') ? loginResponse() : loginResponse().agent)));
    vi.stubGlobal('fetch', fetchMock);
  });
  afterEach(() => { setSession(null); vi.useRealTimers(); vi.resetAllMocks(); vi.unstubAllGlobals(); });

  it('redirects an unauthenticated protected route to login', async () => {
    await mount();
    expect(screen.getByRole('form', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.queryByText('Requested ticket')).not.toBeInTheDocument();
  });

  it.each([['bad email', 'password'], ['alex@example.test', '']])('validates inputs without calling API (%s)', async (email, password) => {
    await mount('/login'); await submit(email, password);
    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.getAllByText(/Enter a/).length).toBeGreaterThan(0);
  });

  it('logs in, returns to the requested page, displays the agent and logs out', async () => {
    await mount(); await submit(' alex@example.test ', 'Test password');
    expect(screen.getByText('Requested ticket')).toBeInTheDocument();
    expect(screen.getByText('Alex Turner')).toBeInTheDocument();
    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/auth/login');
    expect(JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body))).toEqual({ email: 'alex@example.test', password: 'Test password' });
    expect(sessionStorage.getItem('northwind.auth')).not.toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Log out' }));
    expect(screen.getByRole('form', { name: 'Sign in' })).toBeInTheDocument();
    expect(getSession()).toBeNull();
    expect(sessionStorage.getItem('northwind.auth')).toBeNull();
  });

  it('direct login defaults to the ticket list', async () => {
    await mount('/login'); await submit();
    expect(screen.getByText('Ticket list')).toBeInTheDocument();
  });

  it('incorrect credentials show the server message without a session', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ detail: 'Invalid email or password.' }), { status: 401 }));
    await mount(); await submit();
    expect(screen.getByRole('alert')).toHaveTextContent('Invalid email or password.');
    expect(getSession()).toBeNull();
  });

  it('blocks duplicate submission while login is pending', async () => {
    fetchMock.mockImplementation(() => new Promise(() => {}));
    await mount(); await submit();
    expect(screen.getByRole('button', { name: 'Signing in...' })).toBeDisabled();
    await submit();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('attaches the token and clears the session on protected 401', async () => {
    await mount(); await submit();
    fetchMock.mockResolvedValue(new Response('', { status: 401 }));
    await act(async () => { await expect(request<unknown>('/tickets')).rejects.toThrow(); });
    expect(fetchMock.mock.lastCall?.[1]?.headers).toMatchObject({ Authorization: 'Bearer test-token' });
    expect(screen.getByRole('form', { name: 'Sign in' })).toBeInTheDocument();
    expect(getSession()).toBeNull();
  });

  it('ignores stale 401 responses belonging to a previous session', async () => {
    setSession(loginResponse());
    let resolve: (response: Response) => void = () => { throw new Error('Not requested'); };
    fetchMock.mockImplementation(() => new Promise((done) => { resolve = done; }));
    const old = request<unknown>('/tickets');
    const newer = { ...loginResponse(), accessToken: 'new-token' };
    setSession(newer);
    resolve(new Response('', { status: 401 }));
    await expect(old).rejects.toThrow();
    expect(getSession()).toBe(newer);
  });

  it('validates an existing session with me before showing protected content', async () => {
    setSession(loginResponse());
    await mount();
    expect(fetchMock.mock.calls[0]?.[0]).toBe('/api/auth/me');
    expect(screen.getByText('Requested ticket')).toBeInTheDocument();
  });

  it('clears an expired session and redirects without needing an API failure', async () => {
    vi.useFakeTimers();
    setSession({ ...loginResponse(), expiresAtUtc: new Date(Date.now() + 1000).toISOString() });
    await mount();
    await act(async () => { vi.advanceTimersByTime(1001); });
    expect(getSession()).toBeNull();
    expect(screen.getByRole('form', { name: 'Sign in' })).toBeInTheDocument();
  });

  it('rejects invalid login response data safely', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ accessToken: 'token', agent: null })));
    await mount(); await submit();
    expect(screen.getByRole('alert')).toHaveTextContent('invalid login response');
    expect(getSession()).toBeNull();
  });

  it('does not revive a session when logout happens during a pending login', async () => {
    let resolve: (response: Response) => void = () => { throw new Error('Not requested'); };
    fetchMock.mockImplementation(() => new Promise((done) => { resolve = done; }));
    await mount(); await submit();
    setSession(null);
    await act(async () => { resolve(new Response(JSON.stringify(loginResponse()))); });
    expect(getSession()).toBeNull();
    expect(screen.getByRole('form', { name: 'Sign in' })).toBeInTheDocument();
  });

  it('a rejected restored session returns to login', async () => {
    setSession(loginResponse());
    fetchMock.mockResolvedValue(new Response('', { status: 401 }));
    await mount();
    expect(getSession()).toBeNull();
    expect(screen.getByRole('form', { name: 'Sign in' })).toBeInTheDocument();
  });

  it('does not restore an invalid or expired serialized session', () => {
    expect(readSession(null)).toBeNull();
    expect(readSession({ ...loginResponse(), expiresAtUtc: 'bad' })).toBeNull();
    expect(readSession({ ...loginResponse(), expiresAtUtc: new Date(0).toISOString() })).toBeNull();
  });
});
