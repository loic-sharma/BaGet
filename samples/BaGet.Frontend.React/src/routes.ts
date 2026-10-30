/** Maps a ScreenResult `next.screen` ({controller, action}) to a route of this application. */
const table: Record<string, (v: Record<string, unknown>) => string> = {
  'home/index': () => '/',
  'package/index': (v) => `/packages/${encodeURIComponent(String(v.id ?? ''))}/${encodeURIComponent(String(v.version ?? ''))}`,
  'account/login': () => '/sign-in',
  'account/register': () => '/register',
  'account/forgotpassword': () => '/forgot-password',
  'account/resetpassword': () => '/reset-password',
  'account/confirmemail': () => '/confirm-email',
  'adminusers/index': () => '/admin/users',
};

/** Unknown screens fall back to the home page: an SPA covers only the screens it implements. */
export function routeFor(
  screen: { controller?: string | null; action?: string | null },
  routeValues: Record<string, unknown> = {},
): string {
  const key = `${screen.controller ?? ''}/${screen.action ?? ''}`.toLowerCase();
  return (table[key] ?? (() => '/'))(routeValues);
}
