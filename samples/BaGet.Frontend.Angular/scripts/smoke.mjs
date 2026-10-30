// Smoke test against a running BaGet: signs in, lists packages, reads the admin users list, refreshes, signs out.
//   BAGET_URL=http://localhost:6060 BAGET_USER=Admin BAGET_PASSWORD='Admin1234!' node scripts/smoke.mjs
// BAGET_ORIGIN (default http://localhost:4200) is sent as the Origin header like a browser would, so it must be in Cors:AllowedOrigins.
const base = (process.env.BAGET_URL ?? 'http://localhost:6060').replace(/\/+$/, '');
const userName = process.env.BAGET_USER ?? 'Admin';
const password = process.env.BAGET_PASSWORD ?? 'Admin1234!';
const origin = process.env.BAGET_ORIGIN ?? 'http://localhost:4200';

let failures = 0;
const check = (name, ok, detail = '') => {
  console.log(`${ok ? 'ok  ' : 'FAIL'} ${name}${detail ? ` - ${detail}` : ''}`);
  if (!ok) failures++;
};

async function call(method, path, { body, token } = {}) {
  const response = await fetch(base + path, {
    method,
    headers: {
      Origin: origin,
      ...(body ? { 'Content-Type': 'application/json' } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await response.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* not JSON */ }
  return { status: response.status, json, headers: response.headers };
}

// 1. Wrong password: 401 and a ScreenResult, not a token.
const bad = await call('POST', '/api/ui/session', { body: { userName, password: password + 'x' } });
check('wrong password is refused', bad.status === 401 && !bad.json?.model?.accessToken, `HTTP ${bad.status} ${bad.json?.status}`);

// 2. Sign in.
const signIn = await call('POST', '/api/ui/session', { body: { userName, password } });
const session = signIn.json?.model;
check('sign-in answers tokens', signIn.status === 200 && !!session?.accessToken && !!session?.refreshToken, `HTTP ${signIn.status} ${signIn.json?.status}`);
check('CORS allows the origin on the session endpoint', signIn.headers.get('access-control-allow-origin') === origin, String(signIn.headers.get('access-control-allow-origin')));
if (!session?.accessToken) {
  console.log(JSON.stringify(signIn.json));
  process.exit(1);
}

// 3. Packages (anonymous screen, also fine with a token).
const packages = await call('GET', '/api/ui/packages', { token: session.accessToken });
check('packages list', packages.status === 200 && packages.json?.status === 'Ok' && Array.isArray(packages.json?.model?.packages), `${packages.json?.model?.packages?.length ?? '?'} package(s)`);

// 4. Admin screen with the session.
const users = await call('GET', '/api/ui/admin/users', { token: session.accessToken });
check('admin users with the administrator session', users.status === 200 && users.json?.model?.users?.some((u) => u.userName?.toLowerCase() === userName.toLowerCase()), `HTTP ${users.status}`);
const anonymous = await call('GET', '/api/ui/admin/users');
check('admin users without a session is 401', anonymous.status === 401 && anonymous.json?.status === 'SignIn', `HTTP ${anonymous.status} ${anonymous.json?.status}`);

// 5. Refresh gives a new pair.
const refreshed = await call('POST', '/api/ui/session/refresh', { body: { refreshToken: session.refreshToken } });
check('refresh answers a new pair', refreshed.status === 200 && !!refreshed.json?.model?.accessToken, `HTTP ${refreshed.status}`);

// 6. Sign out, then a garbage access token is 401.
const out = await call('POST', '/api/ui/session/sign-out', { body: { refreshToken: session.refreshToken }, token: session.accessToken });
check('sign-out', out.status === 200, `HTTP ${out.status}`);
const garbage = await call('GET', '/api/ui/admin/users', { token: 'bgs.not-a-token' });
check('an invalid token is 401', garbage.status === 401, `HTTP ${garbage.status}`);

console.log(failures ? `${failures} check(s) failed` : 'all checks passed');
process.exit(failures ? 1 : 0);
