import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { routes } from './app.routes';
import { SessionService } from './session/session.service';
import { sessionInterceptor } from './session/session.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([sessionInterceptor])),
    // A refresh token left in sessionStorage by a reload is exchanged for an access token before the first guard runs.
    provideAppInitializer(() => firstValueFrom(inject(SessionService).restore())),
  ],
};
