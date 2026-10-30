using BaGet.Web.Application.Models.Browse;

namespace BaGet.Web.Application.Screens;

/// <summary>The report-abuse screen: the package it is about, what was typed, and whether the report was recorded.</summary>
/// <param name="Id">The package ID.</param>
/// <param name="Version">The package version.</param>
/// <param name="CanReport">Reports are kept in the database, which Azure Table storage cannot do.</param>
/// <param name="Submitted">True once the report was recorded.</param>
/// <param name="Form">What was typed (empty on the first visit).</param>
public sealed record ReportAbuseScreen(string? Id, string? Version, bool CanReport, bool Submitted, ReportAbuseForm Form);
