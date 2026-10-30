import { useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';
import { toScreen, type Screen } from '../api/screen';
import { Field, FormErrors } from '../components/Form';

/** The email link is /reset-password?userId=&code=; `code` is posted back unchanged. */
export function ResetPassword() {
  const [params] = useSearchParams();
  const userId = params.get('userId') ?? '';
  const code = params.get('code') ?? '';
  const [result, setResult] = useState<Screen<unknown> | null>(null);

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    const text = (k: string) => String(f.get(k) ?? '');
    setResult(
      toScreen(await api.POST('/api/ui/account/resetpassword', { body: { userId, code, password: text('password'), confirmPassword: text('confirmPassword') } })),
    );
  }

  if (!userId || !code) return <p className="error">This link is incomplete. Ask for a new one on the <Link to="/forgot-password">forgot password</Link> page.</p>;
  if (result && result.status !== 'Invalid') {
    return (
      <section>
        <h1>Password changed</h1>
        <p><Link to="/sign-in">Sign in</Link> with your new password.</p>
      </section>
    );
  }
  const errors = result?.errors ?? {};
  return (
    <section>
      <h1>Choose a new password</h1>
      <form onSubmit={submit} className="card">
        <Field label="New password" name="password" type="password" errors={errors} autoComplete="new-password" required />
        <Field label="Confirm password" name="confirmPassword" type="password" errors={errors} autoComplete="new-password" required />
        <FormErrors errors={errors} fields={['password', 'confirmPassword']} />
        <button>Change password</button>
      </form>
    </section>
  );
}
