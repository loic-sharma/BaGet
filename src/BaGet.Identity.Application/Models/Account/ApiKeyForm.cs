using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BaGet.Core;

namespace BaGet.Identity.Application.Models.Account;

/// <summary>What the new-key form posts (and the body of <c>POST /api/ui/account/apikeys</c>).</summary>
public class ApiKeyForm
{
    /// <summary>The password, asked again. Needed over <c>/api/ui</c> (a session token or the cookie with an antiforgery header); the screen's form does not post it.</summary>
    public string? Password { get; set; }

    /// <summary>What the key is for.</summary>
    [StringLength(AbstractContext<Microsoft.EntityFrameworkCore.DbContext>.MaxApiKeyDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>The scopes of the new key.</summary>
    public List<string> Scopes { get; set; } = new();

    /// <summary>The package IDs the key applies to.</summary>
    [Display(Name = "Package glob pattern")]
    public string PackageGlobPattern { get; set; } = "*";

    /// <summary>Days until the key expires; none = never.</summary>
    [Range(1, 3650)]
    [Display(Name = "Expires after (days)")]
    public int? ExpiresInDays { get; set; } = 365;

    /// <summary>The keys of the repositories the new key is limited to; none (the default) means every repository.</summary>
    public List<int> RepositoryKeys { get; set; } = new();
}
