using System;

namespace BaGet.Core;

/// <summary>
/// An image repository (<c>team/app</c>) inside a BaGet repository of the Docker format: what <c>docker push host/libs/team/app:1</c>
/// creates (<c>libs</c> is the BaGet repository, <c>team/app</c> the image). Created by the first upload or manifest; it is the
/// unit the access patterns (<c>team/*</c>) and the page of an image speak about.
/// </summary>
public class OciRepository : IRepositoryScoped
{
    public int Key { get; set; }

    /// <summary>The owning BaGet repository; see <see cref="IRepositoryScoped"/>.</summary>
    public int RepositoryKey { get; set; }

    /// <summary>The image name, lower case (<see cref="OciNames.IsValidImage"/>). Unique per BaGet repository.</summary>
    public string? Name { get; set; }

    /// <summary>When the first upload or manifest arrived.</summary>
    public DateTime Created { get; set; }

    /// <summary>The last manifest or tag change.</summary>
    public DateTime Modified { get; set; }
}

/// <summary>
/// A manifest or index of an image, addressed by its digest. The bytes are kept exactly as pushed (the digest is over them) in
/// <see cref="Content"/>; a manifest is small (<see cref="OciOptions.MaxManifestBytes"/>), so it lives in the row and needs no file.
/// </summary>
public class OciManifest
{
    public int Key { get; set; }

    /// <summary>The <see cref="OciRepository.Key"/>. Required; deleting the image deletes its manifests.</summary>
    public int ImageKey { get; set; }

    /// <summary><c>sha256:…</c> of <see cref="Content"/>. Unique per image.</summary>
    public string? Digest { get; set; }

    /// <summary>The manifest's media type (<see cref="OciNames.ImageManifestTypes"/> or <see cref="OciNames.IndexTypes"/>).</summary>
    public string? MediaType { get; set; }

    /// <summary>The artifact type (OCI 1.1): the manifest's <c>artifactType</c>, else its config's media type; null for an index with none.</summary>
    public string? ArtifactType { get; set; }

    /// <summary>The digest of the manifest this one refers to (OCI 1.1 <c>subject</c>): what the referrers API lists it under.</summary>
    public string? SubjectDigest { get; set; }

    /// <summary>Bytes of <see cref="Content"/>.</summary>
    public long Size { get; set; }

    /// <summary>The JSON exactly as pushed (valid UTF-8, round-trips byte for byte).</summary>
    public string? Content { get; set; }

    /// <summary>When it was first pushed to this image.</summary>
    public DateTime Created { get; set; }
}

/// <summary>A tag of an image: a mutable name for a manifest (case-sensitive).</summary>
public class OciTag
{
    public int Key { get; set; }

    /// <summary>The image; part of the unique <c>(ImageKey, Name)</c>.</summary>
    public int ImageKey { get; set; }

    /// <summary>The tag (<see cref="OciNames.IsValidTag"/>).</summary>
    public string? Name { get; set; }

    /// <summary>The <see cref="OciManifest.Key"/> it points at. Required; deleting the manifest deletes its tags.</summary>
    public int ManifestKey { get; set; }

    /// <summary>When the tag was created or last moved.</summary>
    public DateTime Updated { get; set; }
}

/// <summary>
/// A blob (a layer, a config, any artifact file) stored once per BaGet repository, by digest: the file is at
/// <see cref="OciNames.BlobPath"/>. <see cref="RefCount"/> counts the images that hold it (<see cref="OciBlobLink"/>); the file
/// goes when the count reaches zero.
/// </summary>
public class OciBlob : IRepositoryScoped
{
    public int Key { get; set; }

    /// <summary>The owning BaGet repository; see <see cref="IRepositoryScoped"/>.</summary>
    public int RepositoryKey { get; set; }

    /// <summary><c>sha256:…</c>. Unique per BaGet repository.</summary>
    public string? Digest { get; set; }

    /// <summary>Bytes stored.</summary>
    public long Size { get; set; }

    /// <summary>How many images hold the blob (rows of <see cref="OciBlobLink"/>).</summary>
    public int RefCount { get; set; }

    /// <summary>When the blob was first stored.</summary>
    public DateTime Created { get; set; }
}

/// <summary>
/// The fact that an image holds a blob: it was uploaded to the image, mounted into it, or is part of a manifest of it. A blob is
/// served under an image only through its link, so an image whose access is restricted cannot be used to read another image's
/// layers by guessing a digest.
/// </summary>
public class OciBlobLink
{
    public int Key { get; set; }

    /// <summary>The image. Required; deleting the image deletes its links.</summary>
    public int ImageKey { get; set; }

    /// <summary>The <see cref="OciBlob.Key"/>. Required; unique with <see cref="ImageKey"/>.</summary>
    public int BlobKey { get; set; }

    /// <summary>How many manifests of the image use the blob as config or layer; a blob with 0 is an unreferenced upload.</summary>
    public int Manifests { get; set; }

    /// <summary>When the image obtained the blob.</summary>
    public DateTime Created { get; set; }
}

/// <summary>An upload session: the storage upload behind <c>POST /blobs/uploads/</c> until it is committed or abandoned.</summary>
public class OciUpload
{
    public int Key { get; set; }

    /// <summary>The image the upload is for. Required; deleting the image deletes its sessions.</summary>
    public int ImageKey { get; set; }

    /// <summary>The opaque reference handed to the client (the last segment of the upload URL). Unique.</summary>
    public string? Reference { get; set; }

    /// <summary>The <see cref="IStreamingStorageService"/> upload id.</summary>
    public string? StorageUploadId { get; set; }

    /// <summary>When the session started.</summary>
    public DateTime Created { get; set; }

    /// <summary>The last chunk (or the start).</summary>
    public DateTime Updated { get; set; }
}
