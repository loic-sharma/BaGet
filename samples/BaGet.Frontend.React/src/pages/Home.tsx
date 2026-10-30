import { Link, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';
import { toScreen } from '../api/screen';
import { Refusal } from '../components/Form';
import { useScreen } from '../components/useScreen';

/** Package list and search: the twin of the home page, `GET /api/ui/packages?q=&p=`. */
export function Home() {
  const [params, setParams] = useSearchParams();
  const q = params.get('q') ?? '';
  const p = Number(params.get('p') ?? '1') || 1;
  const screen = useScreen(async () => toScreen(await api.GET('/api/ui/packages', { params: { query: { q, p } } })), [q, p]);

  return (
    <section>
      <h1>Packages</h1>
      <form
        role="search"
        onSubmit={(e) => {
          e.preventDefault();
          setParams({ q: String(new FormData(e.currentTarget).get('q') ?? '') });
        }}
      >
        <input name="q" defaultValue={q} placeholder="Search packages" aria-label="Search packages" />
        <button>Search</button>
      </form>
      {!screen && <p>Loading...</p>}
      {screen && !screen.model && <Refusal screen={screen} />}
      {screen?.model && (
        <>
          <ul className="list">
            {(screen.model.packages ?? []).map((pkg) => (
              <li key={pkg.id}>
                <Link to={`/packages/${encodeURIComponent(pkg.id ?? '')}/${encodeURIComponent(pkg.version ?? '')}`}>
                  <strong>{pkg.title || pkg.id}</strong> {pkg.version}
                </Link>
                <span className="muted"> {String(pkg.totalDownloads ?? 0)} downloads</span>
                <p>{pkg.summary || pkg.description}</p>
              </li>
            ))}
            {!screen.model.packages?.length && <li>No package found.</li>}
          </ul>
          <nav className="pager">
            {p > 1 && <button onClick={() => setParams({ q, p: String(p - 1) })}>Previous</button>}
            {(screen.model.packages?.length ?? 0) > 0 && <button onClick={() => setParams({ q, p: String(p + 1) })}>Next</button>}
          </nav>
        </>
      )}
    </section>
  );
}
