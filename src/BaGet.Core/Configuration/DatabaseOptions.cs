using System.ComponentModel.DataAnnotations;

namespace BaGet.Core;

[RestartRequired]
public class DatabaseOptions
{
    /// <summary>
    /// The database engine.
    /// </summary>
    [AllowedSettingValues("AzureTable", "MySql", "PostgreSql", "Sqlite", "SqlServer")]
    public string? Type { get; set; }

    /// <summary>
    /// The connection string of the chosen database engine; for AzureTable, the Azure storage account's
    /// connection string. With MySql, also set Database:MySqlServerVersion to avoid detecting it by connecting.
    /// </summary>
    [Required]
    [SensitiveSetting]
    [SettingExample(nameof(Type), "Sqlite", "Data Source=baget.db")]
    [SettingExample(nameof(Type), "SqlServer", "Server=localhost;Database=BaGet;Trusted_Connection=True;TrustServerCertificate=True")]
    [SettingExample(nameof(Type), "PostgreSql", "Host=localhost;Port=5432;Database=baget;Username=baget;Password=<password>")]
    [SettingExample(nameof(Type), "MySql", "Server=localhost;Port=3306;Database=baget;User=baget;Password=<password>")]
    [SettingExample(nameof(Type), "AzureTable", "DefaultEndpointsProtocol=https;AccountName=<account>;AccountKey=<key>;EndpointSuffix=core.windows.net")]
    public string? ConnectionString { get; set; }

    /// <summary>
    /// The MySQL server version, such as "8.0.0-mysql" or "10.11.0-mariadb". Optional.
    /// </summary>
    /// <remarks>
    /// When this is not set, the version is detected by connecting to the server. Detection
    /// happens while the database options are being built, which also happens when the
    /// Entity Framework design-time tools enumerate the contexts, so an unreachable server
    /// makes commands such as "dotnet ef migrations add" fail even for another provider.
    /// Set this to keep option building offline.
    /// </remarks>
    public string? MySqlServerVersion { get; set; }
}