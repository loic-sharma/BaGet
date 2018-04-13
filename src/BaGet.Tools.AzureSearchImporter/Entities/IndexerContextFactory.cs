using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BaGet.Tools.AzureSearchImporter.Entities
{
    class IndexerContextFactory : IDesignTimeDbContextFactory<IndexerContext>
    {
        public const string ConnectionString = "Data Source=indexer.db";

        public IndexerContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<IndexerContext>();

            optionsBuilder.UseSqlite(ConnectionString);

            return new IndexerContext(optionsBuilder.Options);
        }
    }
}
