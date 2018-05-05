using System.Threading;
using System.Threading.Tasks;
using BaGet.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BaGet.Entities
{
    public abstract class AbstractContext<TContext> : DbContext, IContext where TContext : DbContext
    {
        public const int DefaultMaxStringLength = 4000;

        public const int MaxPackageIdLength = 128;
        public const int MaxPackageVersionLength = 64;
        public const int MaxPackageMinClientVersionLength = 44;
        public const int MaxPackageLanguageLength = 20;
        public const int MaxPackageTitleLength = 256;

        public const int MaxPackageDependencyVersionRangeLength = 256;
        public const int MaxPackageDependencyTargetFrameworkLength = 256;

        public AbstractContext(DbContextOptions<TContext> options)
            : base(options)
        { }

        public DbSet<Package> Packages { get; set; }

        public Task<int> SaveChangesAsync() => SaveChangesAsync(default(CancellationToken));

        public abstract bool IsUniqueConstraintViolationException(DbUpdateException exception);

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Package>(BuildPackageEntity);
            builder.Entity<PackageDependency>(BuildPackageDependencyEntity);
        }

        private void BuildPackageEntity(EntityTypeBuilder<Package> package)
        {
            package.HasKey(p => p.Key);
            package.HasIndex(p => p.Id);
            package.HasIndex(p => new { p.Id, p.VersionString })
                .IsUnique();

            package.Property(p => p.Id)
                .HasMaxLength(MaxPackageIdLength);

            package.Property(p => p.VersionString)
                .HasColumnName("Version")
                .HasMaxLength(MaxPackageVersionLength)
                .IsRequired();

            package.Property(p => p.AuthorsString)
                .HasColumnName("Authors")
                .HasMaxLength(DefaultMaxStringLength);

            package.Property(p => p.IconUrlString)
                .HasColumnName("IconUrl")
                .HasMaxLength(DefaultMaxStringLength);

            package.Property(p => p.LicenseUrlString)
                .HasColumnName("LicenseUrl")
                .HasMaxLength(DefaultMaxStringLength);

            package.Property(p => p.ProjectUrlString)
                .HasColumnName("ProjectUrl")
                .HasMaxLength(DefaultMaxStringLength);

            package.Property(p => p.TagsString)
                .HasColumnName("Tags")
                .HasMaxLength(DefaultMaxStringLength);

            package.Property(p => p.Description).HasMaxLength(DefaultMaxStringLength);
            package.Property(p => p.Language).HasMaxLength(MaxPackageLanguageLength);
            package.Property(p => p.MinClientVersion).HasMaxLength(MaxPackageMinClientVersionLength);
            package.Property(p => p.Summary).HasMaxLength(DefaultMaxStringLength);
            package.Property(p => p.Title).HasMaxLength(MaxPackageTitleLength);

            package.Ignore(p => p.Version);
            package.Ignore(p => p.Authors);
            package.Ignore(p => p.IconUrl);
            package.Ignore(p => p.LicenseUrl);
            package.Ignore(p => p.ProjectUrl);
            package.Ignore(p => p.Tags);
        }

        private void BuildPackageDependencyEntity(EntityTypeBuilder<PackageDependency> dependency)
        {
            dependency.HasKey(d => d.Key);

            dependency.Property(d => d.Id).HasMaxLength(MaxPackageIdLength);
            dependency.Property(d => d.VersionRange).HasMaxLength(MaxPackageDependencyVersionRangeLength);
            dependency.Property(d => d.TargetFramework).HasMaxLength(MaxPackageDependencyTargetFrameworkLength);
        }
    }
}
