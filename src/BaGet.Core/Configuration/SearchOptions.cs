namespace BaGet.Core;

[RestartRequired]
public class SearchOptions
{
    /// <summary>
    /// The search backend.
    /// </summary>
    [AllowedSettingValues("AzureSearch", "Database", "Null")]
    public string? Type { get; set; }
}