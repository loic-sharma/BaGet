using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BaGet.Core;

/// <summary>
/// Decides, for the members of a virtual Docker repository, whether the caller of the request may pull an image from them. The
/// registry's request pipeline (<c>OciAuthorizationFilter</c>) tells it how the caller is found (a registry token carries
/// grants for the members it was issued for, an API key is a principal); anything else (a page, a job) asks the access model for
/// the principal of <see cref="IRequestCaller"/>. Scoped.
/// </summary>
public interface IOciRequestAccess
{
    /// <summary>Sets how a member's pull is decided for this request.</summary>
    void Use(Func<RepositoryInfo, string, CancellationToken, Task<bool>> canPull);

    /// <summary>Whether the caller may pull <paramref name="image"/> from <paramref name="member"/>.</summary>
    Task<bool> CanPullAsync(RepositoryInfo member, string image, CancellationToken cancellationToken);
}

/// <summary>The scoped <see cref="IOciRequestAccess"/>.</summary>
public sealed class OciRequestAccess : IOciRequestAccess
{
    private readonly IRequestCaller _caller;
    private readonly IOciAccess _access;
    private Func<RepositoryInfo, string, CancellationToken, Task<bool>>? _canPull;

    public OciRequestAccess(IRequestCaller caller, IOciAccess access)
    {
        _caller = caller ?? throw new ArgumentNullException(nameof(caller));
        _access = access ?? throw new ArgumentNullException(nameof(access));
    }

    /// <inheritdoc/>
    public void Use(Func<RepositoryInfo, string, CancellationToken, Task<bool>> canPull)
        => _canPull = canPull ?? throw new ArgumentNullException(nameof(canPull));

    /// <inheritdoc/>
    public async Task<bool> CanPullAsync(RepositoryInfo member, string image, CancellationToken cancellationToken)
    {
        if (_canPull != null) return await _canPull(member, image, cancellationToken).ConfigureAwait(false);

        var principal = await _caller.GetAsync(cancellationToken).ConfigureAwait(false);

        return await _access.IsAllowedAsync(principal, member, image, OciActions.Pull, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// <see cref="IOciRegistry"/> of a <see cref="RepositoryKind.Virtual"/> Docker repository: one registry made of its members, local or
/// remote, in resolution order. Each question is asked of the members the caller may pull the image from, one after the other, each in
/// its own DI scope (so its database filter, storage prefix, upstream and rules apply as they do when the member is read directly);
/// a member that fails (its upstream is down) is logged and skipped so the next one can answer.
/// </summary>
/// <remarks>
/// <para><b>Manifests and blobs</b> come from the first member that has them: a tag is the first member's tag, a digest is the same
/// bytes in every member, so a layer is served by whichever member holds it first (a remote member in front of a local one fetches
/// and keeps it; one that does not have it is asked and passes). <b>Tags</b> are the union of the members' (a tag in several members
/// names the first member's manifest when pulled). <b>Referrers</b> are the members' merged. <b>Pages and search</b> merge the members'
/// images.</para>
/// <para><b>Writes</b> go to the deploy repository (<see cref="IVirtualRepositories.DeployTargetAsync"/>) with that repository's rules:
/// the HTTP routes are moved to it before they reach this class (<c>OciAuthorizationFilter</c>); a caller of the service gets the same
/// through this class. Without a deploy repository they are refused (403).</para>
/// </remarks>
public sealed class VirtualOciRegistry : IOciRegistry
{
    private const string NoDeploy = "This virtual repository has no deploy repository, so it takes no pushes or deletes.";

    private readonly IRepositoryContext _repository;
    private readonly IVirtualRepositories _virtuals;
    private readonly IOciRequestAccess _access;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<VirtualOciRegistry> _logger;

    public VirtualOciRegistry(
        IRepositoryContext repository,
        IVirtualRepositories virtuals,
        IOciRequestAccess access,
        IServiceScopeFactory scopes,
        ILogger<VirtualOciRegistry> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _virtuals = virtuals ?? throw new ArgumentNullException(nameof(virtuals));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // The members that answer for the virtual repository and that the caller may pull the image from, in resolution order.
    private async Task<IReadOnlyList<RepositoryInfo>> MembersAsync(string image, CancellationToken cancellationToken)
    {
        var members = await _virtuals.EffectiveMembersAsync(_repository.Current, cancellationToken).ConfigureAwait(false);
        var readable = new List<RepositoryInfo>();
        foreach (var member in members)
        {
            if (await _access.CanPullAsync(member, image, cancellationToken).ConfigureAwait(false)) readable.Add(member);
        }

        return readable;
    }

    // Runs 'action' in a scope pinned to the member. A member that does not know the image answers with nothing; one that fails is
    // skipped. Cancellation and a closed maintenance gate are never swallowed.
    private async Task<T?> InMemberAsync<T>(RepositoryInfo member, Func<IOciRegistry, Task<T?>> action)
        where T : class
    {
        using var scope = _scopes.CreateRepositoryScope(member);
        try
        {
            return await action(scope.ServiceProvider.GetRequiredService<IOciRegistry>()).ConfigureAwait(false);
        }
        catch (OciException e) when (e.Code is OciErrorCode.NameUnknown or OciErrorCode.ManifestUnknown or OciErrorCode.BlobUnknown)
        {
            return null;
        }
        catch (Exception e) when (e is not OperationCanceledException and not MaintenanceException)
        {
            _logger.LogWarning("Member {Member} of virtual repository {Virtual} failed; the next member answers instead: {Reason}", member.Name, _repository.Current.Name, e is OciException ? e.Message : e.GetType().Name);

            return null;
        }
    }

    private async Task<T?> FirstAsync<T>(string image, Func<IOciRegistry, Task<T?>> action, CancellationToken cancellationToken)
        where T : class
    {
        foreach (var member in await MembersAsync(image, cancellationToken).ConfigureAwait(false))
        {
            var answer = await InMemberAsync(member, action).ConfigureAwait(false);
            if (answer != null) return answer;
        }

        return null;
    }

    // ---- reads ----------------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken)
    {
        if (!OciNames.IsValidImage(image)) return false;

        foreach (var member in await MembersAsync(image, cancellationToken).ConfigureAwait(false))
        {
            var exists = await InMemberAsync(member, async registry => await registry.ImageExistsAsync(image, cancellationToken).ConfigureAwait(false) ? new object() : null).ConfigureAwait(false);
            if (exists != null) return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public Task<OciBlobInfo?> GetBlobInfoAsync(string image, string digest, CancellationToken cancellationToken)
        => FirstAsync(image, registry => registry.GetBlobInfoAsync(image, digest, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    /// <remarks>The member's scope lives as long as the blob is read: a remote member keeps the blob it streams in its own database.</remarks>
    public async Task<OciBlobContent?> OpenBlobAsync(string image, string digest, long offset, long? length, CancellationToken cancellationToken)
    {
        foreach (var member in await MembersAsync(image, cancellationToken).ConfigureAwait(false))
        {
            var scope = _scopes.CreateRepositoryScope(member);
            try
            {
                var content = await scope.ServiceProvider.GetRequiredService<IOciRegistry>().OpenBlobAsync(image, digest, offset, length, cancellationToken).ConfigureAwait(false);
                if (content != null)
                {
                    content.Owner = scope;

                    return content;
                }
            }
            catch (OciException e) when (e.Code is OciErrorCode.NameUnknown or OciErrorCode.ManifestUnknown or OciErrorCode.BlobUnknown)
            {
            }
            catch (Exception e) when (e is not OperationCanceledException and not MaintenanceException)
            {
                _logger.LogWarning("Member {Member} of virtual repository {Virtual} failed; the next member answers instead: {Reason}", member.Name, _repository.Current.Name, e is OciException ? e.Message : e.GetType().Name);
            }
            catch
            {
                scope.Dispose();

                throw;
            }

            scope.Dispose();
        }

        return null;
    }

    /// <inheritdoc/>
    public Task<OciManifestContent?> GetManifestAsync(string image, string reference, CancellationToken cancellationToken)
        => FirstAsync(image, registry => registry.GetManifestAsync(image, reference, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public async Task<OciTagList?> ListTagsAsync(string image, int? count, string? last, CancellationToken cancellationToken)
    {
        var known = false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in await MembersAsync(image, cancellationToken).ConfigureAwait(false))
        {
            var list = await InMemberAsync(member, registry => registry.ListTagsAsync(image, null, null, cancellationToken)).ConfigureAwait(false);
            if (list == null) continue;

            known = true;
            names.UnionWith(list.Tags);
        }

        return known ? RemoteOciRegistry.Page(image, names.ToList(), count, last) : null;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OciDescriptor>?> GetReferrersAsync(string image, string digest, string? artifactType, CancellationToken cancellationToken)
    {
        IReadOnlyList<OciDescriptor>? merged = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in await MembersAsync(image, cancellationToken).ConfigureAwait(false))
        {
            var list = await InMemberAsync(member, registry => registry.GetReferrersAsync(image, digest, artifactType, cancellationToken)).ConfigureAwait(false);
            if (list == null) continue;

            merged ??= new List<OciDescriptor>();
            foreach (var descriptor in list)
            {
                if (seen.Add(descriptor.Digest)) ((List<OciDescriptor>)merged).Add(descriptor);
            }
        }

        return merged;
    }

    /// <inheritdoc/>
    public async Task<ResolutionOutcome> ProbeAsync(string image, string reference, CancellationToken cancellationToken)
    {
        var first = ResolutionOutcome.NotFound;
        foreach (var member in await MembersAsync(image, cancellationToken).ConfigureAwait(false))
        {
            using var scope = _scopes.CreateRepositoryScope(member);
            ResolutionOutcome outcome;
            try
            {
                outcome = await scope.ServiceProvider.GetRequiredService<IOciRegistry>().ProbeAsync(image, reference, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException and not MaintenanceException)
            {
                outcome = ResolutionOutcome.Unavailable;
            }

            if (outcome is ResolutionOutcome.Stored) return outcome;
            if (first == ResolutionOutcome.NotFound) first = outcome;
        }

        return first;
    }

    // ---- writes: the deploy repository ----------------------------------------------------------------------------------

    private async Task<T> InDeployAsync<T>(Func<IOciRegistry, Task<T>> action, CancellationToken cancellationToken)
    {
        var target = await _virtuals.DeployTargetAsync(_repository.Current, cancellationToken).ConfigureAwait(false)
                     ?? throw new OciException(OciErrorCode.Denied, NoDeploy);

        using var scope = _scopes.CreateRepositoryScope(target);

        return await action(scope.ServiceProvider.GetRequiredService<IOciRegistry>()).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<OciUploadState?> GetUploadAsync(string image, string reference, CancellationToken cancellationToken)
        => InDeployAsync(registry => registry.GetUploadAsync(image, reference, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<OciUploadState> StartUploadAsync(string image, CancellationToken cancellationToken)
        => InDeployAsync(registry => registry.StartUploadAsync(image, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<OciUploadState> AppendAsync(string image, string reference, long? offset, Stream chunk, CancellationToken cancellationToken)
        => InDeployAsync(registry => registry.AppendAsync(image, reference, offset, chunk, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<OciBlobStored> CompleteUploadAsync(string image, string reference, string digest, long? offset, Stream? lastChunk, CancellationToken cancellationToken)
        => InDeployAsync(registry => registry.CompleteUploadAsync(image, reference, digest, offset, lastChunk, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<bool> CancelUploadAsync(string image, string reference, CancellationToken cancellationToken)
        => InDeployAsync(registry => registry.CancelUploadAsync(image, reference, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<OciBlobStored> PutBlobAsync(string image, string digest, Stream content, CancellationToken cancellationToken)
        => InDeployAsync(registry => registry.PutBlobAsync(image, digest, content, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<bool> MountBlobAsync(string image, string digest, string fromImage, CancellationToken cancellationToken)
        => InDeployAsync(registry => registry.MountBlobAsync(image, digest, fromImage, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<OciManifestPut> PutManifestAsync(string image, string reference, string? mediaType, byte[] content, CancellationToken cancellationToken)
        => InDeployAsync(registry => registry.PutManifestAsync(image, reference, mediaType, content, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<bool> DeleteManifestAsync(string image, string digest, CancellationToken cancellationToken)
        => InDeployAsync(registry => registry.DeleteManifestAsync(image, digest, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<bool> DeleteTagAsync(string image, string tag, CancellationToken cancellationToken)
        => InDeployAsync(registry => registry.DeleteTagAsync(image, tag, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<bool> DeleteBlobAsync(string image, string digest, CancellationToken cancellationToken)
        => InDeployAsync(registry => registry.DeleteBlobAsync(image, digest, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<bool> DeleteImageAsync(string image, CancellationToken cancellationToken)
        => InDeployAsync(registry => registry.DeleteImageAsync(image, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<OciPruneResult> PruneAsync(string image, CancellationToken cancellationToken)
        => InDeployAsync(registry => registry.PruneAsync(image, cancellationToken), cancellationToken);

    // ---- the pages: the members' images ---------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<OciImagePage> ListImagesAsync(string? text, int skip, int take, Func<string, CancellationToken, Task<bool>>? canRead, CancellationToken cancellationToken)
    {
        // Each member lists the images the caller may pull from it; the first member's entry wins an image.
        var members = await _virtuals.EffectiveMembersAsync(_repository.Current, cancellationToken).ConfigureAwait(false);
        var byName = new Dictionary<string, OciImageSummary>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            var page = await InMemberAsync(member, registry => registry.ListImagesAsync(
                text,
                0,
                250,
                async (name, ct) => await _access.CanPullAsync(member, name, ct).ConfigureAwait(false)
                                    && (canRead == null || await canRead(name, ct).ConfigureAwait(false)),
                cancellationToken)!).ConfigureAwait(false);
            if (page == null) continue;

            foreach (var summary in page.Images) byName.TryAdd(summary.Name, summary);
        }

        var all = byName.Values.OrderBy(i => i.Name, StringComparer.Ordinal).ToList();

        return new OciImagePage(all.Count, all.Skip(Math.Max(skip, 0)).Take(Math.Clamp(take, 0, 250)).ToList());
    }

    /// <inheritdoc/>
    public Task<OciImageInfo?> GetImageAsync(string image, CancellationToken cancellationToken)
        => FirstAsync(image, registry => registry.GetImageAsync(image, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<OciManifestDetail?> GetManifestDetailAsync(string image, string digest, CancellationToken cancellationToken)
        => FirstAsync(image, registry => registry.GetManifestDetailAsync(image, digest, cancellationToken), cancellationToken);
}

/// <summary>
/// The <see cref="IOciRegistry"/> everything else asks for: it forwards each call to the registry of the request's repository
/// (<see cref="OciRegistryService"/> for a local one, <see cref="RemoteOciRegistry"/>, <see cref="VirtualOciRegistry"/>), so the routes
/// and pages never ask which. Resolved per call, because a write to a virtual repository continues in its deploy repository.
/// </summary>
public sealed class RepositoryOciRegistry : IOciRegistry
{
    private readonly IRepositoryContext _repository;
    private readonly IServiceProvider _services;

    public RepositoryOciRegistry(IRepositoryContext repository, IServiceProvider services)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    private IOciRegistry Inner => _repository.Current.Kind switch
    {
        RepositoryKind.Remote => _services.GetRequiredService<RemoteOciRegistry>(),
        RepositoryKind.Virtual => _services.GetRequiredService<VirtualOciRegistry>(),
        _ => _services.GetRequiredService<OciRegistryService>(),
    };

    /// <inheritdoc/>
    public Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken) => Inner.ImageExistsAsync(image, cancellationToken);

    /// <inheritdoc/>
    public Task<OciBlobInfo?> GetBlobInfoAsync(string image, string digest, CancellationToken cancellationToken) => Inner.GetBlobInfoAsync(image, digest, cancellationToken);

    /// <inheritdoc/>
    public Task<OciBlobContent?> OpenBlobAsync(string image, string digest, long offset, long? length, CancellationToken cancellationToken) => Inner.OpenBlobAsync(image, digest, offset, length, cancellationToken);

    /// <inheritdoc/>
    public Task<OciManifestContent?> GetManifestAsync(string image, string reference, CancellationToken cancellationToken) => Inner.GetManifestAsync(image, reference, cancellationToken);

    /// <inheritdoc/>
    public Task<OciTagList?> ListTagsAsync(string image, int? count, string? last, CancellationToken cancellationToken) => Inner.ListTagsAsync(image, count, last, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<OciDescriptor>?> GetReferrersAsync(string image, string digest, string? artifactType, CancellationToken cancellationToken) => Inner.GetReferrersAsync(image, digest, artifactType, cancellationToken);

    /// <inheritdoc/>
    public Task<OciUploadState?> GetUploadAsync(string image, string reference, CancellationToken cancellationToken) => Inner.GetUploadAsync(image, reference, cancellationToken);

    /// <inheritdoc/>
    public Task<OciUploadState> StartUploadAsync(string image, CancellationToken cancellationToken) => Inner.StartUploadAsync(image, cancellationToken);

    /// <inheritdoc/>
    public Task<OciUploadState> AppendAsync(string image, string reference, long? offset, Stream chunk, CancellationToken cancellationToken) => Inner.AppendAsync(image, reference, offset, chunk, cancellationToken);

    /// <inheritdoc/>
    public Task<OciBlobStored> CompleteUploadAsync(string image, string reference, string digest, long? offset, Stream? lastChunk, CancellationToken cancellationToken) => Inner.CompleteUploadAsync(image, reference, digest, offset, lastChunk, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> CancelUploadAsync(string image, string reference, CancellationToken cancellationToken) => Inner.CancelUploadAsync(image, reference, cancellationToken);

    /// <inheritdoc/>
    public Task<OciBlobStored> PutBlobAsync(string image, string digest, Stream content, CancellationToken cancellationToken) => Inner.PutBlobAsync(image, digest, content, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> MountBlobAsync(string image, string digest, string fromImage, CancellationToken cancellationToken) => Inner.MountBlobAsync(image, digest, fromImage, cancellationToken);

    /// <inheritdoc/>
    public Task<OciManifestPut> PutManifestAsync(string image, string reference, string? mediaType, byte[] content, CancellationToken cancellationToken) => Inner.PutManifestAsync(image, reference, mediaType, content, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> DeleteManifestAsync(string image, string digest, CancellationToken cancellationToken) => Inner.DeleteManifestAsync(image, digest, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> DeleteTagAsync(string image, string tag, CancellationToken cancellationToken) => Inner.DeleteTagAsync(image, tag, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> DeleteBlobAsync(string image, string digest, CancellationToken cancellationToken) => Inner.DeleteBlobAsync(image, digest, cancellationToken);

    /// <inheritdoc/>
    public Task<ResolutionOutcome> ProbeAsync(string image, string reference, CancellationToken cancellationToken) => Inner.ProbeAsync(image, reference, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> DeleteImageAsync(string image, CancellationToken cancellationToken) => Inner.DeleteImageAsync(image, cancellationToken);

    /// <inheritdoc/>
    public Task<OciPruneResult> PruneAsync(string image, CancellationToken cancellationToken) => Inner.PruneAsync(image, cancellationToken);

    /// <inheritdoc/>
    public Task<OciImagePage> ListImagesAsync(string? text, int skip, int take, Func<string, CancellationToken, Task<bool>>? canRead, CancellationToken cancellationToken) => Inner.ListImagesAsync(text, skip, take, canRead, cancellationToken);

    /// <inheritdoc/>
    public Task<OciImageInfo?> GetImageAsync(string image, CancellationToken cancellationToken) => Inner.GetImageAsync(image, cancellationToken);

    /// <inheritdoc/>
    public Task<OciManifestDetail?> GetManifestDetailAsync(string image, string digest, CancellationToken cancellationToken) => Inner.GetManifestDetailAsync(image, digest, cancellationToken);
}
