import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionService } from './session.service';

/** Signed-in routes: anyone else goes to the sign-in page, which returns here afterwards. */
export const authGuard: CanActivateFn = (_route, state) => {
  const session = inject(SessionService);
  const router = inject(Router);
  return session.isSignedIn() || router.createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } });
};
