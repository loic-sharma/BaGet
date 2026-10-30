import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { nextRoute, toScreen, type Screen } from '../api/screen';
import type { SessionModel } from '../api/session';
import { useAuth } from '../auth';
import { basePath, baseUrl } from '../config';
import { Field, FormErrors } from '../components/Form';

type Step = 'password' | 'code' | 'recovery';

const OIDC_STATE_KEY = 'baget.oidc-state';

/** Saves a random state, then leaves for the provider `id`; BaGet returns to `/sign-in?state=...#exchange_token=...`. */
function startExternalSignIn(id: string) {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  const state = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
  sessionStorage.setItem(OIDC_STATE_KEY, state);
  const returnUrl = encodeURIComponent(`${window.location.origin}${basePath}/sign-in?state=${state}`);
  window.location.href = `${baseUrl}/api/ui/session/external/${encodeURIComponent(id)}?returnUrl=${returnUrl}`;
}

export function SignIn() {
  const auth = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [step, setStep] = useState<Step>('password');
  const [ticket, setTicket] = useState('');
  const [result, setResult] = useState<Screen<SessionModel> | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  // One button per external provider the server offers (the sign-in screen model lists them).
  const [providers, setProviders] = useState<{ id: string; displayName: string }[]>([]);
  const from = (location.state as { from?: string } | null)?.from ?? '/';

  /** A session answer is either tokens (done) or a two-factor ticket (ask for the code). */
  function handle(s: Screen<SessionModel>) {
    setResult(s);
    const m = s.model;
    if (m?.twoFactorRequired && m.twoFactorTicket) {
      setTicket(m.twoFactorTicket);
      setStep('code');
    } else if (m?.accessToken) {
      auth.adopt(m);
      navigate(nextRoute(s) ?? from, { replace: true });
    }
  }

  useEffect(() => {
    // Anonymous endpoint; a failure (or an old server) just means no external buttons.
    void api
      .GET('/api/ui/session/providers', {})
      .then((r) => setProviders(toScreen(r).model?.providers ?? []))
      .catch(() => setProviders([]));
  }, []);

  // External sign-in returns to /sign-in#exchange_token=...: take the token out of the address bar
  // at once (history and Referer), then exchange it. #notice / #error carry messages instead.
  const handled = useRef(false);
  useEffect(() => {
    if (handled.current || !location.hash) return;
    const h = new URLSearchParams(location.hash.slice(1));
    const exchangeToken = h.get('exchange_token');
    if (!exchangeToken && !h.get('notice') && !h.get('error')) return;
    handled.current = true;
    // The callback must carry the random state saved before the redirect (it binds the answer to this browser).
    const expected = sessionStorage.getItem(OIDC_STATE_KEY);
    sessionStorage.removeItem(OIDC_STATE_KEY);
    const stateOk = !!expected && new URLSearchParams(location.search).get('state') === expected;
    history.replaceState(null, '', location.pathname);
    if (!stateOk) {
      setResult({ status: 'Invalid', http: 400, model: null, errors: { '': ['External sign-in refused: the state does not match. Start again.'] }, next: null });
      return;
    }
    setNotice(h.get('notice') ?? null);
    if (h.get('error')) setResult({ status: 'Invalid', http: 400, model: null, errors: { '': [h.get('error')!] }, next: null });
    if (exchangeToken) {
      setBusy(true);
      void api
        .POST('/api/ui/session/exchange', { body: { exchangeToken } })
        .then((r) => handle(toScreen(r)))
        .finally(() => setBusy(false));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.hash]);

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    const text = (k: string) => String(f.get(k) ?? '');
    setBusy(true);
    try {
      if (step === 'password') {
        handle(toScreen(await api.POST('/api/ui/session', { body: { userName: text('userName'), password: text('password') } })));
      } else if (step === 'code') {
        handle(toScreen(await api.POST('/api/ui/session/two-factor', { body: { ticket, code: text('code') } })));
      } else {
        handle(toScreen(await api.POST('/api/ui/session/recovery-code', { body: { ticket, recoveryCode: text('recoveryCode') } })));
      }
    } finally {
      setBusy(false);
    }
  }

  const errors = result?.errors ?? {};

  return (
    <section>
      <h1>Sign in</h1>
      {notice && <p className="notice">{notice}</p>}
      <form onSubmit={submit} className="card">
        {step === 'password' && (
          <>
            <Field label="User name" name="userName" errors={errors} autoComplete="username" required />
            <Field label="Password" name="password" type="password" errors={errors} autoComplete="current-password" required />
          </>
        )}
        {step === 'code' && <Field label="Authenticator code" name="code" errors={errors} inputMode="numeric" autoComplete="one-time-code" required />}
        {step === 'recovery' && <Field label="Recovery code" name="recoveryCode" errors={errors} required />}
        <FormErrors errors={errors} fields={['userName', 'password', 'code', 'recoveryCode']} />
        <button disabled={busy}>{busy ? 'Signing in...' : 'Sign in'}</button>
        {step === 'code' && (
          <button type="button" className="link" onClick={() => setStep('recovery')}>
            Use a recovery code
          </button>
        )}
      </form>
      <p>
        <Link to="/register">Create an account</Link> | <Link to="/forgot-password">Forgot your password?</Link>
        {providers.map((p) => (
          <span key={p.id}>
            {' '}
            |{' '}
            <button type="button" className="link" onClick={() => startExternalSignIn(p.id)}>
              Sign in with {p.displayName}
            </button>
          </span>
        ))}
      </p>
    </section>
  );
}
