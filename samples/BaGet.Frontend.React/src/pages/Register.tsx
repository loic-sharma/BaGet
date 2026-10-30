import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { toScreen, type Screen } from '../api/screen';
import { useAuth } from '../auth';
import { Field, FormErrors } from '../components/Form';
import type { components } from '../api/schema';

type RegisterModel = components['schemas']['RegisterViewModel'];

export function Register() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [result, setResult] = useState<Screen<RegisterModel> | null>(null);

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    const text = (k: string) => String(f.get(k) ?? '');
    const s = toScreen(
      await api.POST('/api/ui/account/register', {
        body: { userName: text('userName'), email: text('email'), password: text('password'), confirmPassword: text('confirmPassword') },
      }),
    );
    setResult(s);
    // The account could sign in at once: the answer carries its tokens.
    if (s.status !== 'Invalid' && s.model?.session?.accessToken) {
      auth.adopt(s.model.session);
      navigate('/', { replace: true });
    }
  }

  const m = result?.status === 'Invalid' ? null : result?.model;
  if (m && !m.session) {
    return (
      <section>
        <h1>Account created</h1>
        {m.confirmationSent && <p>We sent you an email: follow its link to confirm your address.</p>}
        {m.awaitingApproval && <p>An administrator has to approve your account before you can sign in.</p>}
        {!m.confirmationSent && !m.awaitingApproval && <p>You can now <Link to="/sign-in">sign in</Link>.</p>}
      </section>
    );
  }
  const errors = result?.errors ?? {};
  return (
    <section>
      <h1>Register</h1>
      <form onSubmit={submit} className="card">
        <Field label="User name" name="userName" errors={errors} autoComplete="username" required />
        <Field label="Email" name="email" type="email" errors={errors} autoComplete="email" />
        <Field label="Password" name="password" type="password" errors={errors} autoComplete="new-password" required />
        <Field label="Confirm password" name="confirmPassword" type="password" errors={errors} autoComplete="new-password" required />
        <FormErrors errors={errors} fields={['userName', 'email', 'password', 'confirmPassword']} />
        <button>Register</button>
      </form>
    </section>
  );
}
