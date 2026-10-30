import { createContext, useContext, useEffect, useState, useSyncExternalStore, type ReactNode } from 'react';
import { api, refreshSession } from './api/client';
import { toScreen } from './api/screen';
import { sessionStore, type SessionModel } from './api/session';

interface Auth {
  /** False until the stored refresh token (if any) has been tried once. */
  ready: boolean;
  userName: string | null;
  signedIn: boolean;
  /** Stores the tokens of a finished sign-in. */
  adopt(m: SessionModel): void;
  signOut(everywhere?: boolean): Promise<void>;
}

const Ctx = createContext<Auth | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const s = useSyncExternalStore(sessionStore.subscribe, sessionStore.get);
  const [ready, setReady] = useState(false);

  // A reload loses the in-memory access token: get a new one from the refresh token in sessionStorage.
  useEffect(() => {
    const { accessToken, refreshToken } = sessionStore.get();
    if (accessToken || !refreshToken) {
      setReady(true);
      return;
    }
    void refreshSession().finally(() => setReady(true));
  }, []);

  const value: Auth = {
    ready,
    userName: s.userName,
    signedIn: s.accessToken !== null,
    adopt: (m) => sessionStore.adopt(m),
    async signOut(everywhere = false) {
      const { refreshToken } = sessionStore.get();
      // "Everywhere" changes the account's security stamp, ending every token and cookie session.
      // Plain sign-out revokes nothing server side: the client simply forgets its tokens.
      try {
        toScreen(await api.POST('/api/ui/session/sign-out', { body: { everywhere, refreshToken: refreshToken ?? undefined } }));
      } finally {
        sessionStore.clear();
      }
    },
  };
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useAuth(): Auth {
  const v = useContext(Ctx);
  if (!v) throw new Error('useAuth outside AuthProvider');
  return v;
}
