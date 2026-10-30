import { Link, useLocation, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';
import { toScreen } from '../api/screen';
import { Refusal } from '../components/Form';
import { useScreen } from '../components/useScreen';

/**
 * `GET /api/ui/admin/users`: needs an administrator's session. The other answers are shown as they come:
 * SignIn (401, no or expired session), Forbidden (403, signed in but not an administrator).
 */
export function AdminUsers() {
  const location = useLocation();
  const [params, setParams] = useSearchParams();
  const q = params.get('q') ?? '';
  const p = Number(params.get('p') ?? '1') || 1;
  const screen = useScreen(async () => toScreen(await api.GET('/api/ui/admin/users', { params: { query: { q, p } } })), [q, p]);

  if (!screen) return <p>Loading...</p>;
  if (!screen.model || screen.status === 'SignIn' || screen.status === 'Forbidden') {
    return (
      <Refusal screen={screen}>
        {screen.status === 'SignIn' && <Link to="/sign-in" state={{ from: location.pathname }}>Sign in</Link>}
        {screen.status === 'Forbidden' && <p>This screen is for administrators.</p>}
      </Refusal>
    );
  }
  const m = screen.model;
  return (
    <section>
      <h1>Users</h1>
      <form
        role="search"
        onSubmit={(e) => {
          e.preventDefault();
          setParams({ q: String(new FormData(e.currentTarget).get('q') ?? '') });
        }}
      >
        <input name="q" defaultValue={q} placeholder="Filter users" aria-label="Filter users" />
        <button>Filter</button>
      </form>
      <table>
        <thead>
          <tr><th>User</th><th>Email</th><th>Created</th><th>Role</th></tr>
        </thead>
        <tbody>
          {(m.users ?? []).map((u) => (
            <tr key={u.userName}>
              <td>{u.userName}{u.pendingApproval ? ' (pending)' : ''}</td>
              <td>{u.email}</td>
              <td>{u.created ? new Date(u.created).toLocaleDateString() : ''}</td>
              <td>{u.isAdministrator ? 'administrator' : 'user'}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <nav className="pager">
        {p > 1 && <button onClick={() => setParams({ q, p: String(p - 1) })}>Previous</button>}
        {p < Number(m.pageCount ?? 1) && <button onClick={() => setParams({ q, p: String(p + 1) })}>Next</button>}
      </nav>
    </section>
  );
}
