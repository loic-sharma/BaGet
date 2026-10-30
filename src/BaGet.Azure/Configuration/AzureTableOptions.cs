using System.ComponentModel.DataAnnotations;
using BaGet.Core;

namespace BaGet.Azure;

[RestartRequired]
public class AzureTableOptions
{
    [Required]
    [SensitiveSetting]
    public string? ConnectionString { get; set; }
}