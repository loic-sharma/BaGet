import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { toScreen } from '../api/screen';
import { Refusal } from '../components/Form';
import { useScreen } from '../components/useScreen';

/** `GET /api/ui/packages/{id}` (latest) or `/api/ui/packages/{id}/{version}`. A missing package is a NotFound screen (404), not an exception. */
export function PackageDetail() {
  const { id = '', version } = useParams();
  const screen = useScreen(async () =>
      toScreen(
        version === undefined
          ? await api.GET('/api/ui/packages/{id}', { params: { path: { id } } })
          : await api.GET('/api/ui/packages/{id}/{version}', { params: { path: { id, version } } }),
      ),
    [id, version],
  );

  if (!screen) return <p>Loading...</p>;
  const m = screen.model;
  if (!m || m.found === false) return <Refusal screen={screen}><Link to="/">Back to the list</Link></Refusal>;
  return (
    <section>
      <h1>
        {m.package?.id} <small>{m.package?.version}</small>
      </h1>
      <p>{m.package?.description}</p>
      <p className="muted">
        {String(m.totalDownloads ?? 0)} downloads, by {(m.package?.authors ?? []).join(', ') || 'unknown'}
      </p>
      {m.installTabs?.map((t) => (
        <details key={t.name}>
          <summary>{t.name}</summary>
          <pre>{(t.lines ?? []).join('\n')}</pre>
        </details>
      ))}
      <h2>Versions</h2>
      <ul>
        {(m.versions ?? []).map((v) => (
          <li key={v.version}>
            {v.selected ? <strong>{v.version}</strong> : <Link to={`/packages/${encodeURIComponent(id)}/${encodeURIComponent(v.version ?? '')}`}>{v.version}</Link>}
          </li>
        ))}
      </ul>
      {m.readmeHtml && <p className="muted">This package has a readme (HTML, not rendered here: sanitise it before injecting it).</p>}
    </section>
  );
}
