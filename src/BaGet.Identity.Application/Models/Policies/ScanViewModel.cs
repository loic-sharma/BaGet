using BaGet.Core;

namespace BaGet.Identity.Application.Models.Policies;

/// <summary>What the <em>Scan now</em> screen (<c>/scan-artifact</c>) shows; filled by the screen's workflow.</summary>
public sealed class ScanViewModel
{
    /// <summary>The artifact name (NuGet id, npm name, Maven <c>group:artifact</c>, Docker image).</summary>
    public string? Name { get; internal set; }

    /// <summary>The version (Docker: the manifest digest).</summary>
    public string? Version { get; internal set; }

    /// <summary>Where the link back goes: the package page the user came from (a local address only).</summary>
    public string? ReturnUrl { get; internal set; }

    /// <summary>What happened, for the screen to show.</summary>
    public string? Message { get; internal set; }

    /// <summary>Whether <see cref="Message"/> reports a refusal.</summary>
    public bool Failed { get; internal set; }

    /// <summary>The result just stored, with its findings, or null.</summary>
    public ScanResult? Result { get; internal set; }

    /// <summary>The scanner status of the repository, for the explanation when it cannot run.</summary>
    public ScanEngineStatus? Engine { get; internal set; }

    /// <summary>The address the link back leads to (the return address when it is local, else the home page).</summary>
    public string BackUrl { get; internal set; } = "/";
}
