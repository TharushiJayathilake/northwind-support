import { useRef, useState } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { toErrorMessage } from '../../api/client';
import { Field } from '../../components/Field';
import { useAuth } from './context';

export function LoginPage() {
  const { login, session, isChecking } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const state: unknown = location.state;
  const target = typeof state === 'object' && state !== null && 'from' in state &&
    typeof state.from === 'string' && state.from.startsWith('/') && !state.from.startsWith('//') &&
    !state.from.startsWith('/login') ? state.from : '/tickets';
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string>();
  const [isSaving, setIsSaving] = useState(false);
  const pending = useRef(false);

  if (session && !isChecking) return <Navigate to={target} replace />;

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (pending.current) return;
    const validation: Record<string, string> = {};
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim()) || email.trim().length > 256)
      validation.email = 'Enter a valid email address.';
    if (!password || password.length > 4096) validation.password = 'Enter a password of up to 4096 characters.';
    setErrors(validation); setError(undefined);
    if (Object.keys(validation).length) return;
    pending.current = true; setIsSaving(true);
    try {
      if (await login(email, password)) await navigate(target, { replace: true });
    } catch (caught) { setError(toErrorMessage(caught, 'Could not sign in.')); }
    finally { pending.current = false; setIsSaving(false); }
  };

  return <main className="app__main login-page">
    <header className="page-header"><div><h1>Northwind Support</h1><p>Sign in</p></div></header>
    <form className="form" aria-label="Sign in" noValidate onSubmit={submit}>
      <Field id="login-email" label="Email" error={errors.email}>
        {(props) => <input {...props} type="email" autoComplete="username" value={email}
          disabled={isSaving} onChange={(event) => setEmail(event.target.value)} />}
      </Field>
      <Field id="login-password" label="Password" error={errors.password}>
        {(props) => <input {...props} type="password" autoComplete="current-password" value={password}
          disabled={isSaving} onChange={(event) => setPassword(event.target.value)} />}
      </Field>
      {error && <p className="field__error" role="alert">{error}</p>}
      <button className="button button--primary" type="submit" disabled={isSaving || isChecking}>
        {isSaving ? 'Signing in...' : 'Sign in'}
      </button>
    </form>
  </main>;
}
