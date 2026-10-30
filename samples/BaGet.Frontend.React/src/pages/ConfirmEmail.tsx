import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';
import { toScreen } from '../api/screen';
import { useScreen } from '../components/useScreen';
import { Field, FormErrors } from '../components/Form';

/** The email link is /confirm-email?userId=&code=[&email=]; the GET confirms. */
export function ConfirmEmail() {
  const [params] = useSearchParams();
  const userId = params.get('userId') ?? '';
  const code = params.get('code') ?? '';
  const email = params.get('email') ?? undefined;
  const screen = useScreen(
    async () => toScreen(await api.GET('/api/ui/account/confirmemail', { params: { query: { userId, code, email } } })),
    [userId, code, email],
  );
  const [resent, setResent] = useState(false);
  const [resendErrors, setResendErrors] = useState<Record<string, string[]>>({});

  if (!screen) return <p>Confirming...</p>;
  if (screen.model?.succeeded) {
    return (
      <section>
        <h1>Email confirmed</h1>
        {screen.model.pendingApproval ? <p>An administrator still has to approve your account.</p> : <p><Link to="/sign-in">Sign in</Link>.</p>}
      </section>
    );
  }
  return (
    <section>
      <h1>This link did not work</h1>
      <p>It may have expired or been used already. Enter your user name or email to receive a new one.</p>
      {resent ? (
        <p>If an unconfirmed account matches, a new link is on its way.</p>
      ) : (
        <form
          className="card"
          onSubmit={async (e) => {
            e.preventDefault();
            const identifier = String(new FormData(e.currentTarget).get('identifier') ?? '');
            const s = toScreen(await api.POST('/api/ui/account/resend-confirmation', { body: { identifier } }));
            if (s.status === 'Invalid') setResendErrors(s.errors);
            else setResent(true);
          }}
        >
          <Field label="User name or email" name="identifier" errors={resendErrors} required />
          <FormErrors errors={resendErrors} fields={['identifier']} />
          <button>Resend the link</button>
        </form>
      )}
    </section>
  );
}
