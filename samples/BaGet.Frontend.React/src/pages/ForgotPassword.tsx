import { useState, type FormEvent } from 'react';
import { api } from '../api/client';
import { toScreen, type Screen } from '../api/screen';
import { Field, FormErrors } from '../components/Form';

export function ForgotPassword() {
  const [result, setResult] = useState<Screen<unknown> | null>(null);

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const identifier = String(new FormData(e.currentTarget).get('identifier') ?? '');
    setResult(toScreen(await api.POST('/api/ui/account/forgotpassword', { body: { identifier } })));
  }

  // The server answers the same whoever the name belongs to, so does the page.
  if (result && result.status !== 'Invalid') {
    return (
      <section>
        <h1>Check your email</h1>
        <p>If an account matches, a link to choose a new password is on its way.</p>
      </section>
    );
  }
  const errors = result?.errors ?? {};
  return (
    <section>
      <h1>Forgot password</h1>
      <form onSubmit={submit} className="card">
        <Field label="User name or email" name="identifier" errors={errors} required />
        <FormErrors errors={errors} fields={['identifier']} />
        <button>Send the link</button>
      </form>
    </section>
  );
}
