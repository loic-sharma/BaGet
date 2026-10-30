using System;

namespace BaGet.Identity.Application.Services.Organizations;

/// <summary>An organization as the administrator's list shows it.</summary>
/// <param name="Name">The name.</param>
/// <param name="Created">When it was created.</param>
/// <param name="Members">How many members it has.</param>
/// <param name="Administrators">How many of them administer it.</param>
/// <param name="PackageIds">How many package IDs it owns, counted per repository.</param>
public record OrganizationSummary(string? Name, DateTime Created, int Members, int Administrators, int PackageIds);
