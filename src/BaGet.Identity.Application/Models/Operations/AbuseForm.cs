namespace BaGet.Identity.Application.Models.Operations;

/// <summary>A decision on an abuse report.</summary>
/// <param name="Key">The report's number.</param>
/// <param name="All">Whether the list to come back to shows the resolved reports too.</param>
public sealed record AbuseForm(int Key, bool All);
