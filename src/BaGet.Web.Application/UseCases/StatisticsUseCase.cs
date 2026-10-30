using System;
using System.Threading;
using System.Threading.Tasks;
using BaGet.Core;
using BaGet.Identity.Application.Screens;
using BaGet.Web.Application.Models.Browse;

namespace BaGet.Web.Application.UseCases;

/// <summary>The statistics screen (<c>/stats</c>, <c>/r/{repo}/stats</c> in the request's repository): the feed's totals and lists.</summary>
public sealed class StatisticsUseCase
{
    private readonly StatisticsService _statistics;

    /// <summary>Creates the use case.</summary>
    public StatisticsUseCase(StatisticsService statistics)
    {
        _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
    }

    /// <summary>The feed's statistics; the model's <c>Statistics</c> is null when the database cannot report them.</summary>
    public async Task<ScreenResult<StatisticsViewModel>> GetAsync(CancellationToken cancellationToken)
        => ScreenResult<StatisticsViewModel>.Ok(
            new StatisticsViewModel(_statistics.IsSupported ? await _statistics.GetAsync(cancellationToken) : null));
}
