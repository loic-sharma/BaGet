using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BaGet.Database.SqlServer;

/// <summary>
/// Builds a context for the Entity Framework design-time tools.
/// </summary>
/// <remarks>
/// Without this the tools build the application's service provider to find the contexts,
/// which configures every database provider that is registered - including ones whose
/// server is unreachable or whose driver does not match the installed Entity Framework
/// version. A factory keeps "dotnet ef" offline and independent of the other providers.
/// </remarks>
public class SqlServerContextFactory : IDesignTimeDbContextFactory<SqlServerContext>
{
    public SqlServerContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SqlServerContext>()
            .UseSqlServer(@"Server=(localdb)\mssqllocaldb;Database=BaGet")
            .Options;

        return new SqlServerContext(options);
    }
}