using System;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>
/// Who is asking, for code in Core that must filter what it shows by the caller's rights (a virtual repository reads
/// only the members the caller may read). The host's request pipeline calls <see cref="Use"/> with a resolver; the
/// credentials are read only when something asks. Scoped. Without a resolver the caller is anonymous.
/// </summary>
public interface IRequestCaller
{
    /// <summary>The caller's principal, or null for an anonymous one.</summary>
    Task<BaGetPrincipal?> GetAsync(CancellationToken cancellationToken);

    /// <summary>Sets how the caller is found (once per request or job).</summary>
    void Use(Func<CancellationToken, Task<BaGetPrincipal?>> resolver);
}

/// <summary>The scoped <see cref="IRequestCaller"/>; the answer is kept for the scope.</summary>
public sealed class RequestCaller : IRequestCaller
{
    private Func<CancellationToken, Task<BaGetPrincipal?>>? _resolver;
    private Task<BaGetPrincipal?>? _resolved;

    /// <inheritdoc/>
    public Task<BaGetPrincipal?> GetAsync(CancellationToken cancellationToken)
    {
        if (_resolver == null) return Task.FromResult<BaGetPrincipal?>(null);

        // Resolved once per scope (a request or job).
        return _resolved ??= _resolver(cancellationToken);
    }

    /// <inheritdoc/>
    public void Use(Func<CancellationToken, Task<BaGetPrincipal?>> resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _resolved = null;
    }
}
