using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace BaGet.Core;

/// <summary>The <c>import downloads</c> command as an <see cref="IAdminOperation"/>.</summary>
public sealed class DownloadsImportOperation : IAdminOperation
{
    public string Name => "downloads-import";

    public string Title => "Import download counts";

    public string Description => "Imports the download counts from the configured downloads file into the repository's packages.";

    public bool IsAvailable(IServiceProvider services)
        => services.GetRequiredService<IContext>() is not NullContext;

    public async Task<string> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await services.GetRequiredService<DownloadsImporter>().ImportAsync(cancellationToken).ConfigureAwait(false);

        return "Download counts imported.";
    }
}
