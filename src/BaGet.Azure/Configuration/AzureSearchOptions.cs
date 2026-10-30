using System.ComponentModel.DataAnnotations;
using BaGet.Core;

namespace BaGet.Azure;

[RestartRequired]
public class AzureSearchOptions
{
    [Required]
    public string? AccountName { get; set; }

    [Required]
    [SensitiveSetting]
    public string? ApiKey { get; set; }
}