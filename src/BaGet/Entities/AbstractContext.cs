using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace BaGet.Entities
{
    public abstract class AbstractContext<TContext> : DbContext, IContext where TContext : DbContext
    {
        public AbstractContext(DbContextOptions<TContext> options)
            : base(options)
        { }

        public DbSet<Package> Packages { get; set; }

        public Task<int> SaveChangesAsync() => SaveChangesAsync(default(CancellationToken));

        public abstract bool IsUniqueConstraintViolationException(DbUpdateException exception);

        protected override void OnModelCreating(ModelBuilder builder)
        {
            BuildPackageEntity(builder);

            builder.Entity<PackageDependencyGroup>()
                .HasKey(g => g.Key);

            builder.Entity<PackageDependency>()
                .HasKey(d => d.Key);
        }

        private void BuildPackageEntity(ModelBuilder builder)
        {
            builder.Entity<Package>()
                .HasKey(p => p.Key);

            builder.Entity<Package>()
                .HasIndex(p => p.Id);

            builder.Entity<Package>()
                .Property(p => p.VersionString)
                .HasColumnName("Version");

            builder.Entity<Package>()
                .Property(p => p.AuthorsString)
                .HasColumnName("Authors");

            builder.Entity<Package>()
                .HasIndex(p => new { p.Id, p.VersionString })
                .IsUnique();

            builder.Entity<Package>()
                .Property(p => p.IconUrlString)
                .HasColumnName("IconUrl");

            builder.Entity<Package>()
                .Property(p => p.LicenseUrlString)
                .HasColumnName("LicenseUrl");

            builder.Entity<Package>()
                .Property(p => p.ProjectUrlString)
                .HasColumnName("ProjectUrl");

            builder.Entity<Package>()
                .Property(p => p.TagsString)
                .HasColumnName("Tags");

            builder.Entity<Package>()
                .Ignore(p => p.Version)
                .Ignore(p => p.Authors)
                .Ignore(p => p.IconUrl)
                .Ignore(p => p.LicenseUrl)
                .Ignore(p => p.ProjectUrl)
                .Ignore(p => p.Tags);
        }
    }
}
