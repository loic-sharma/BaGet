using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using BaGet.Web.Application.Screens;

namespace BaGet.Web.Application.UseCases;

/// <summary>
/// The Terms of Use (<c>/terms</c>) and Privacy Policy (<c>/privacy</c>) screens: the operator-supplied Markdown files
/// (<c>Site:TermsOfUsePath</c>, <c>Site:PrivacyPolicyPath</c>). Not found while a file is not configured, is not an allowed Markdown path or does not exist.
/// </summary>
public sealed class LegalUseCase
{
    private readonly IOptionsSnapshot<SiteOptions> _site;
    private readonly IHostEnvironment _environment;

    /// <summary>Creates the use case.</summary>
    public LegalUseCase(IOptionsSnapshot<SiteOptions> site, IHostEnvironment environment)
    {
        _site = site ?? throw new ArgumentNullException(nameof(site));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <summary>The Privacy Policy.</summary>
    public Task<ScreenResult<LegalScreen>> PrivacyAsync(CancellationToken cancellationToken)
        => ShowAsync(_site.Value.PrivacyPolicyPath, "Privacy Policy", cancellationToken);

    /// <summary>The Terms of Use.</summary>
    public Task<ScreenResult<LegalScreen>> TermsAsync(CancellationToken cancellationToken)
        => ShowAsync(_site.Value.TermsOfUsePath, "Terms of Use", cancellationToken);

    private async Task<ScreenResult<LegalScreen>> ShowAsync(string? path, string title, CancellationToken cancellationToken)
    {
        var content = await LegalContent.ReadAsync(path, _environment, cancellationToken);

        return content == null
            ? ScreenResult<LegalScreen>.NotFound()
            : ScreenResult<LegalScreen>.Ok(new LegalScreen(title, content.Value ?? string.Empty));
    }
}
