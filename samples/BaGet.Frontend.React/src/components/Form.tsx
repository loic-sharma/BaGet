import type { InputHTMLAttributes, ReactNode } from 'react';
import { refusalText, type Screen } from '../api/screen';

type Errors = Record<string, string[]>;

/** Messages for one field; server names may be cased differently (userName / UserName). */
export function fieldErrors(errors: Errors, name: string): string[] {
  const hit = Object.keys(errors).find((k) => k.toLowerCase() === name.toLowerCase());
  return hit ? errors[hit] : [];
}

/** Form-level messages: the "" key, plus any key that matches no field of the form. */
export function FormErrors({ errors, fields }: { errors: Errors; fields: string[] }) {
  const known = fields.map((f) => f.toLowerCase());
  const messages = Object.entries(errors)
    .filter(([k]) => k === '' || !known.includes(k.toLowerCase()))
    .flatMap(([, v]) => v);
  if (!messages.length) return null;
  return (
    <ul className="errors" role="alert">
      {messages.map((m, i) => (
        <li key={i}>{m}</li>
      ))}
    </ul>
  );
}

export function Field({ label, name, errors, ...input }: { label: string; name: string; errors: Errors } & InputHTMLAttributes<HTMLInputElement>) {
  const own = fieldErrors(errors, name);
  return (
    <label className="field">
      <span>{label}</span>
      <input name={name} aria-invalid={own.length > 0} {...input} />
      {own.map((m, i) => (
        <small key={i} className="error">
          {m}
        </small>
      ))}
    </label>
  );
}

/** The answer a screen could not use (401 / 403 / 404 / server error), as a message with a hint. */
export function Refusal({ screen, children }: { screen: Screen<unknown>; children?: ReactNode }) {
  return (
    <div className="refusal" role="alert">
      <strong>{screen.status === 'SignIn' ? 'Signed out' : screen.status === 'Forbidden' ? 'Forbidden' : 'Problem'}</strong>
      <p>{refusalText(screen)}</p>
      {children}
    </div>
  );
}
