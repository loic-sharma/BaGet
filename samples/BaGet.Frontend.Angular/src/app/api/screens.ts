import { Injectable, signal } from '@angular/core';
import { AbstractControl, FormGroup } from '@angular/forms';
import { Router, UrlTree } from '@angular/router';
import type { ScreenNext, ScreenResult } from './api-types';

type RouteValues = Record<string, unknown>;

/**
 * A screen of the web UI is `controller/action` (`AdminUsers/Index`, `ScreenCatalog` on the server). The application maps the ones
 * it has to its own routes; the rest fall back to the home page. The SPA routes of the emails (`confirm-email`, `reset-password`,
 * `sign-in`) are the server's `Frontend:SpaLinks` routes.
 */
const SCREEN_ROUTES: Record<string, (values: RouteValues) => unknown[]> = {
  'Home/Index': () => ['/packages'],
  'Package/Show': (v) => ['/packages', ...[v['id'], v['version']].filter((x) => x != null && x !== '')],
  'Account/Login': () => ['/sign-in'],
  'Account/Register': () => ['/register'],
  'Account/ForgotPassword': () => ['/forgot-password'],
  'Account/ResetPassword': () => ['/reset-password'],
  'Account/ConfirmEmail': () => ['/confirm-email'],
  'AdminUsers/Index': () => ['/admin/users'],
};

/** Route values that are part of a screen's path in the SPA; everything else of `routeValues` becomes the query string. */
const PATH_VALUES = new Set(['id', 'version']);

/** The router address of `ScreenResult.next`, or `null` when the application has no such screen. */
export function nextToUrlTree(router: Router, next: ScreenNext | null | undefined): UrlTree | null {
  if (!next) {
    return null;
  }
  if (next.localUrl) {
    // A local address of the server's own screens ("/packages", "~/"): only the home page makes sense here.
    return router.parseUrl(next.localUrl === '~/' || next.localUrl === '/' ? '/packages' : safeLocalUrl(next.localUrl) ?? '/packages');
  }
  const screen = next.screen;
  const build = screen ? SCREEN_ROUTES[`${screen.controller}/${screen.action}`] : undefined;
  if (!build) {
    return null;
  }
  const values = (next.routeValues ?? {}) as RouteValues;
  const queryParams: Record<string, string> = {};
  for (const [key, value] of Object.entries(values)) {
    if (!PATH_VALUES.has(key) && value != null && value !== '' && key !== 'handler') {
      queryParams[key] = String(value);
    }
  }
  return router.createUrlTree(build(values), { queryParams, fragment: next.fragment ?? undefined });
}

/** A return address is followed only when it is a path of this application ("/x", never "//host" or "https://..."). */
export function safeLocalUrl(url: string | null | undefined): string | null {
  return url && url.startsWith('/') && !url.startsWith('//') && !url.includes('\\') ? url : null;
}

/** The flash message a finished action leaves for the next screen (`next.message` / `next.error`). */
@Injectable({ providedIn: 'root' })
export class Flash {
  readonly message = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  show(message?: string | null, error?: string | null): void {
    this.message.set(message ?? null);
    this.error.set(error ?? null);
  }

  clear(): void {
    this.show();
  }
}

/** Follows `next` of a result (flash included). Returns false when the application has no such screen. */
export function follow(router: Router, flash: Flash, result: ScreenResult, fallback: unknown[] = ['/packages']): void {
  flash.show(result.next?.message, result.next?.error);
  const target = nextToUrlTree(router, result.next);
  void router.navigateByUrl(target ?? router.createUrlTree(fallback));
}

/** The messages of the whole form (`errors[""]`), then of fields no control has. */
export function formLevelErrors(result: ScreenResult, form?: FormGroup): string[] {
  const errors = result.errors ?? {};
  const messages = [...(errors[''] ?? [])];
  for (const [field, list] of Object.entries(errors)) {
    if (field !== '' && (!form || !findControl(form, field))) {
      messages.push(...list);
    }
  }
  return messages;
}

/**
 * Puts `ScreenResult.errors` on a reactive form: a field's messages become the `server` error of its control (matched
 * case-insensitively, so `Password` and `password` both find it), and the control is touched so the message shows. Returns the
 * messages that belong to no control (the whole form's `""` and unknown fields), to print above the form.
 */
export function applyServerErrors(form: FormGroup, result: ScreenResult): string[] {
  for (const control of Object.values(form.controls)) {
    if (control.hasError('server')) {
      const { server: _server, ...rest } = control.errors ?? {};
      control.setErrors(Object.keys(rest).length ? rest : null);
    }
  }
  for (const [field, list] of Object.entries(result.errors ?? {})) {
    const control = field === '' ? null : findControl(form, field);
    if (control) {
      control.setErrors({ ...control.errors, server: list });
      control.markAsTouched();
    }
  }
  return formLevelErrors(result, form);
}

function findControl(form: FormGroup, field: string): AbstractControl | null {
  const wanted = field.toLowerCase();
  const name = Object.keys(form.controls).find((key) => key.toLowerCase() === wanted);
  return name ? form.controls[name] : null;
}
