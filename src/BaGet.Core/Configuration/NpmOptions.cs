using System;

namespace BaGet.Core;

/// <summary>
/// How npm repositories behave (<c>Npm:*</c>); both settings are read on each request, so a change applies without a restart.
/// </summary>
public class NpmOptions
{
    /// <summary>
    /// How long after a version was published its owner may still unpublish it with Deploy alone, as npmjs lets an author take
    /// back a mistake within 72 hours. Later, unpublishing needs the Delete action (and ownership, and the
    /// <c>package:unlist</c> scope of a key). A whole package counts as young only while every version is. Zero or negative:
    /// the Delete action is always required.
    /// </summary>
    public TimeSpan UnpublishWindow { get; set; } = TimeSpan.FromHours(72);

    /// <summary>
    /// The largest <c>npm publish</c> request body accepted, in bytes (the tarball travels inside the JSON as base64, so a
    /// tarball is about three quarters of it). Default 512 MiB; zero or negative: no limit beyond the web server's own.
    /// </summary>
    public long MaxPublishBytes { get; set; } = 512L * 1024 * 1024;
}
