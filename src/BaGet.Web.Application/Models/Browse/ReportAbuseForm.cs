using System.ComponentModel.DataAnnotations;
using BaGet.Core;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Web.Application.Models.Browse;

/// <summary>What the reporter fills in. The field names (Reason, Details, ReporterEmail) are the contract of the form and of the JSON body.</summary>
public sealed class ReportAbuseForm
{
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
}
