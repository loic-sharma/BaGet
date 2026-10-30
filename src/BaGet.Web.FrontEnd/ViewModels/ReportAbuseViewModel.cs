using System.ComponentModel.DataAnnotations;
using BaGet.Core;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Web.V3.ViewModels;

/// <summary>The report-abuse form and its outcome. The field names (Reason, Details, ReporterEmail) are the contract of the form.</summary>
public sealed class ReportAbuseViewModel
{
    /// <summary>The package ID (route).</summary>
    public string? Id { get; set; }

    /// <summary>The package version (route).</summary>
    public string? Version { get; set; }

    /// <summary>One of <see cref="AbuseReport.Reasons"/>.</summary>
    [Required]
    public string? Reason { get; set; }

    /// <summary>What the reporter saw.</summary>
    [Required]
    [StringLength(AbstractContext<DbContext>.DefaultMaxStringLength)]
    public string? Details { get; set; }

    /// <summary>The address of the reporter, optional.</summary>
    [EmailAddress]
    [StringLength(AbstractContext<DbContext>.MaxEmailLength)]
    [Display(Name = "Your email (optional)")]
    public string? ReporterEmail { get; set; }

    /// <summary>True once the report was recorded. Never bound from the form (the page's property had a private setter).</summary>
    [BindNever, ValidateNever]
    public bool Submitted { get; set; }

    /// <summary>Reports are kept in the database, which Azure Table storage cannot do.</summary>
    [BindNever, ValidateNever]
    public bool CanReport { get; set; }
}
