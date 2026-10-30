// Smoke test: signs in with a session token and lists packages, using openapi-fetch like the app does.
// Usage: node scripts/smoke.mjs <baseUrl> [userName] [password] [origin]
// A script sends no Origin header; pass a 4th argument to send one and see the CORS rules at work.
import createClient from 'openapi-fetch';

const [baseUrl = 'http://localhost:6060', userName = 'Admin', password = 'Admin1234!', origin] = process.argv.slice(2);
const headers = origin ? { Origin: origin } : {};
const api = createClient({ baseUrl: baseUrl.replace(/\/$/, ''), headers });

function check(name, ok, detail = '') {
  console.log(`${ok ? 'ok  ' : 'FAIL'} ${name} ${detail}`);
  if (!ok) process.exitCode = 1;
}

// Anonymous: the home twin is public; the admin one is 401 SignIn.
const anon = await api.GET('/api/ui/packages', { params: { query: { q: '', p: 1 } } });
check('anonymous packages list', anon.response.ok, `HTTP ${anon.response.status}, status=${anon.data?.status}`);
const denied = await api.GET('/api/ui/admin/users', { params: { query: {} } });
check('anonymous admin users is 401', denied.response.status === 401, `HTTP ${denied.response.status}, status=${denied.error?.status}`);

const signIn = await api.POST('/api/ui/session', { body: { userName, password } });
const m = signIn.data?.model;
check('sign in', signIn.response.ok && !!m?.accessToken, `HTTP ${signIn.response.status}, status=${signIn.data?.status}, twoFactor=${m?.twoFactorRequired ?? false}`);
if (!m?.accessToken) process.exit(1);

const auth = { Authorization: `Bearer ${m.accessToken}` };
const list = await api.GET('/api/ui/packages', { params: { query: { q: '', p: 1 } }, headers: auth });
check('signed-in packages list', list.response.ok, `${list.data?.model?.packages?.length ?? 0} packages`);
const users = await api.GET('/api/ui/admin/users', { params: { query: {} }, headers: auth });
check('admin users', users.response.ok, `${users.data?.model?.users?.length ?? 0} users`);

const refreshed = await api.POST('/api/ui/session/refresh', { body: { refreshToken: m.refreshToken } });
check('refresh', refreshed.response.ok && !!refreshed.data?.model?.accessToken, `HTTP ${refreshed.response.status}`);

const out = await api.POST('/api/ui/session/sign-out', { body: { refreshToken: m.refreshToken }, headers: auth });
check('sign out', out.response.ok, `HTTP ${out.response.status}`);
