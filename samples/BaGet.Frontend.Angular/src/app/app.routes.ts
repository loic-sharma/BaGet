import { Routes } from '@angular/router';
import { authGuard } from './session/auth.guard';

/**
 * `sign-in`, `confirm-email` and `reset-password` are the routes BaGet's emails link to (`Frontend:SpaLinks`,
 * `{Frontend:SpaBasePath}/confirm-email?userId=&code=[&email=]`, `.../reset-password?userId=&code=`).
 */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'packages' },
  { path: 'packages', loadComponent: () => import('./pages/packages').then((m) => m.Packages) },
  { path: 'packages/:id', loadComponent: () => import('./pages/package-detail').then((m) => m.PackageDetail) },
  { path: 'packages/:id/:version', loadComponent: () => import('./pages/package-detail').then((m) => m.PackageDetail) },
  { path: 'sign-in', loadComponent: () => import('./pages/sign-in').then((m) => m.SignIn) },
  { path: 'sign-in/external', loadComponent: () => import('./pages/external-sign-in').then((m) => m.ExternalSignIn) },
  { path: 'register', loadComponent: () => import('./pages/register').then((m) => m.Register) },
  { path: 'forgot-password', loadComponent: () => import('./pages/forgot-password').then((m) => m.ForgotPassword) },
  { path: 'reset-password', loadComponent: () => import('./pages/reset-password').then((m) => m.ResetPassword) },
  { path: 'confirm-email', loadComponent: () => import('./pages/confirm-email').then((m) => m.ConfirmEmail) },
  { path: 'admin/users', canActivate: [authGuard], loadComponent: () => import('./pages/admin-users').then((m) => m.AdminUsers) },
  { path: '**', redirectTo: 'packages' },
];
