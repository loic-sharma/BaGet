using System;
using System.Threading;
using System.Threading.Tasks;

namespace BaGet.Core;

/// <summary>Takes back a manifest put that the scanning policy refused.</summary>
public static class OciPushUndo
{
    /// <summary>
    /// A refused put leaves nothing behind: a manifest that was new is removed, and a tag the put created or moved (also onto a manifest
    /// the image already held) goes back to where it pointed. Used by the registry route and by pull replication, so both refuse alike.
    /// </summary>
    /// <param name="registry">The registry, working in the repository the put went to.</param>
    /// <param name="image">The image.</param>
    /// <param name="reference">The tag or digest the manifest was put under.</param>
    /// <param name="put">What the put reported.</param>
    public static async Task UndoAsync(IOciRegistry registry, string image, string reference, OciManifestPut put)
    {
        if (registry == null) throw new ArgumentNullException(nameof(registry));
        if (put == null) throw new ArgumentNullException(nameof(put));

        var ct = CancellationToken.None;
        if (put.Created) await registry.DeleteManifestAsync(image, put.Digest, ct).ConfigureAwait(false);
        if (OciNames.LooksLikeDigest(reference)) return;

        if (put.PreviousDigest == null)
        {
            await registry.DeleteTagAsync(image, reference, ct).ConfigureAwait(false);
        }
        else if (!string.Equals(put.PreviousDigest, put.Digest, StringComparison.Ordinal))
        {
            var previous = await registry.GetManifestAsync(image, put.PreviousDigest, ct).ConfigureAwait(false);
            if (previous != null) await registry.PutManifestAsync(image, reference, previous.MediaType, previous.Content, ct).ConfigureAwait(false);
        }
    }
}
