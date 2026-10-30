using System.Collections.Concurrent;

namespace BaGet.Core;

/// <summary>
/// The container images being scanned right now, process-wide: the scanner pulls the image through this registry, and that pull must
/// not be refused as "unscanned" (see <see cref="IScanGate.IsScannerPullAsync"/>).
/// In memory per process; in a cluster (<c>Ha:Enabled</c>) each image is also marked in the shared store, so the node that receives the
/// scanner's pull sees a scan another node started (set <see cref="ScanningOptions.TrivyRegistryUser"/> to avoid depending on it).
/// </summary>
internal static class ScanInFlight
{
    private static readonly ConcurrentDictionary<(int, string, string), int> Running = new();

    /// <summary>How long a shared mark lasts if its node dies mid-scan.</summary>
    private static readonly System.TimeSpan SharedTtl = System.TimeSpan.FromMinutes(30);

    private static string SharedKey(int repositoryKey, string name, string version) => $"scan-inflight:{repositoryKey}:{name}:{version}";

    public static bool Contains(int repositoryKey, string name, string version) => Running.ContainsKey((repositoryKey, name, version));

    /// <summary>Whether the image is being scanned here or, with a shared store, on any node.</summary>
    public static async System.Threading.Tasks.Task<bool> ContainsAsync(int repositoryKey, string name, string version, IClusterStore? shared, System.Threading.CancellationToken cancellationToken)
    {
        if (Contains(repositoryKey, name, version)) return true;

        return shared != null && await shared.GetAsync(SharedKey(repositoryKey, name, version), cancellationToken).ConfigureAwait(false) != null;
    }

    /// <summary>Marks the image as being scanned until the returned handle is disposed.</summary>
    public static System.IDisposable Begin(int repositoryKey, string name, string version, IClusterStore? shared = null)
    {
        var key = (repositoryKey, name, version);
        Running.AddOrUpdate(key, 1, (_, n) => n + 1);
        if (shared != null)
        {
            try { shared.Set(SharedKey(repositoryKey, name, version), "1", SharedTtl); }
            catch (System.Exception) { /* the local mark still covers a pull that reaches this node */ }
        }

        return new Handle(key, shared);
    }

    private sealed class Handle : System.IDisposable
    {
        private readonly (int, string, string) _key;
        private readonly IClusterStore? _shared;
        private int _done;

        public Handle((int, string, string) key, IClusterStore? shared)
        {
            _key = key;
            _shared = shared;
        }

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _done, 1) != 0) return;

            if (_shared != null)
            {
                try { _shared.Delete(SharedKey(_key.Item1, _key.Item2, _key.Item3)); }
                catch (System.Exception) { /* expires by itself */ }
            }

            while (Running.TryGetValue(_key, out var n))
            {
                if (n <= 1 ? Running.TryRemove(new System.Collections.Generic.KeyValuePair<(int, string, string), int>(_key, n)) : Running.TryUpdate(_key, n - 1, n)) break;
            }
        }
    }
}
