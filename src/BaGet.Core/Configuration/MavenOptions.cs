namespace BaGet.Core;

/// <summary>
/// How Maven repositories behave (<c>Maven:*</c>); read on each request, so a change applies without a restart.
/// </summary>
public class MavenOptions
{
    /// <summary>
    /// The largest single file a deploy may store, in bytes (the request body is spooled to a temporary file while it is
    /// hashed, so the limit also bounds the disk used by one request). Default 1 GiB; zero or negative: no limit beyond the
    /// web server's own.
    /// </summary>
    public long MaxFileBytes { get; set; } = 1024L * 1024 * 1024;
}
