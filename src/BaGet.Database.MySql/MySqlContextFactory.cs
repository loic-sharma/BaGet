using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BaGet.Database.MySql;

/// <summary>
/// Builds a context for the Entity Framework design-time tools.
/// </summary>
/// <remarks>
/// Without this the tools build the application's service provider to find the contexts,
/// which configures every database provider that is registered - including ones whose
/// server is unreachable or whose driver does not match the installed Entity Framework
/// version. A factory keeps "dotnet ef" offline and independent of the other providers.
/// </remarks>
public class MySqlContextFactory : IDesignTimeDbContextFactory<MySqlContext>
{
    public MySqlContext CreateDbContext(string[] args)
    {
        // Parse rather than detect: detecting the version connects to the server, which
        // would put the design-time tools back off line.
        var options = new DbContextOptionsBuilder<MySqlContext>()
            .UseMySql(
                "Server=localhost;Database=baget;User=baget",
                ServerVersion.Parse("8.0.0-mysql"))
            .Options;

        return new MySqlContext(options);
    }
}