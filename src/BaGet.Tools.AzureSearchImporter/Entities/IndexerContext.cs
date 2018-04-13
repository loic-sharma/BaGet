using Microsoft.EntityFrameworkCore;

namespace BaGet.Tools.AzureSearchImporter.Entities
{
    public class IndexerContext : DbContext
    {
        public IndexerContext(DbContextOptions<IndexerContext> options)
            : base(options)
        {}

        public DbSet<PackageId> PackageIds { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<PackageId>()
                .HasKey(p => p.Key);

            builder.Entity<PackageId>()
                .Property(p => p.Value)
                .HasColumnType("TEXT COLLATE NOCASE");
        }
    }
}
