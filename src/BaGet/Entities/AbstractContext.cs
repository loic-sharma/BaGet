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
        public const int MaxRepositoryTypeLength = 100;

        public const int MaxPackageDependencyVersionRangeLength = 256;
        public const int MaxPackageDependencyTargetFrameworkLength = 256;

        public AbstractContext(DbContextOptions<TContext> options)
            : base(options)
        { }

        public DbSet<Package> Packages { get; set; }
        public DbSet<SourceCodeAssembly> SourceCodeAssemblies { get; set; }
        public DbSet<SourceCodeType> SourceCodeTypes { get; set; }
        public DbSet<SourceCodeMember> SourceCodeMembers { get; set; }
        public DbSet<PackageDependency> PackageDependencies { get; set; }

        public Task<int> SaveChangesAsync() => SaveChangesAsync(default(CancellationToken));

        public abstract bool IsUniqueConstraintViolationException(DbUpdateException exception);

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Package>(BuildPackageEntity);
            builder.Entity<PackageDependency>(BuildPackageDependencyEntity);
            builder.Entity<SourceCodeAssembly>(BuildSourceCodeAssemblyEntity);
            builder.Entity<SourceCodeType>(BuildSourceCodeTypeEntity);
            builder.Entity<SourceCodeMember>(BuildSourceCodeMemberEntity);
        }

        private void BuildSourceCodeAssemblyEntity(EntityTypeBuilder<SourceCodeAssembly> package)
        {
            package.HasKey(p => p.Key);
            //package.HasMany(s => s.Types)
            //    .WithOne(s => s.Assembly)
            //    .HasForeignKey(s => s.AssemblyKey)
            //    .HasPrincipalKey(s => s.Key);

            //package.HasOne(s => s.Package)
            //    .WithMany(s => s.SourceCodeAssemblies)
            //    .HasForeignKey(s => s.PackageKey)
            //    .HasPrincipalKey(s => s.Key);
        }

        private void BuildSourceCodeTypeEntity(EntityTypeBuilder<SourceCodeType> package)
        {
            package.HasKey(p => p.Key);
            //package.HasMany(s => s.Members)
            //    .WithOne(s => s.Type)
            //    .HasForeignKey(s => s.TypeKey)
            //    .HasPrincipalKey(s => s.Key);
        }

        private void BuildSourceCodeMemberEntity(EntityTypeBuilder<SourceCodeMember> package)
        {
            package.HasKey(p => p.Key);
        }

        private void BuildPackageEntity(EntityTypeBuilder<Package> package)
        {
            package.HasKey(p => p.Key);
            package.HasIndex(p => p.Id);
            package.HasIndex(p => new { p.Id, p.VersionString })
                .IsUnique();

            package.Property(p => p.Id)
                .HasMaxLength(MaxPackageIdLength)
                .IsRequired();

            package.Property(p => p.VersionString)
                .HasColumnName("Version")
                .HasMaxLength(MaxPackageVersionLength)
                .IsRequired();

            package.Property(p => p.Authors)
                .HasConversion(StringArrayToJsonConverter.Instance)
                .HasMaxLength(DefaultMaxStringLength);

            package.Property(p => p.IconUrl)
                .HasConversion(UriToStringConverter.Instance)
                .HasMaxLength(DefaultMaxStringLength);

            package.Property(p => p.LicenseUrl)
                .HasConversion(UriToStringConverter.Instance)
                .HasMaxLength(DefaultMaxStringLength);

            package.Property(p => p.ProjectUrl)
                .HasConversion(UriToStringConverter.Instance)
                .HasMaxLength(DefaultMaxStringLength);

            package.Property(p => p.RepositoryUrl)
                .HasConversion(UriToStringConverter.Instance)
                .HasMaxLength(DefaultMaxStringLength);

            package.Property(p => p.Tags)
                .HasConversion(StringArrayToJsonConverter.Instance)
                .HasMaxLength(DefaultMaxStringLength);

            package.Property(p => p.Description).HasMaxLength(DefaultMaxStringLength);
            package.Property(p => p.Language).HasMaxLength(MaxPackageLanguageLength);
            package.Property(p => p.MinClientVersion).HasMaxLength(MaxPackageMinClientVersionLength);
            package.Property(p => p.Summary).HasMaxLength(DefaultMaxStringLength);
            package.Property(p => p.Title).HasMaxLength(MaxPackageTitleLength);
            package.Property(p => p.RepositoryType).HasMaxLength(MaxRepositoryTypeLength);

            package.Ignore(p => p.Version);
            package.Ignore(p => p.IconUrlString);
            package.Ignore(p => p.LicenseUrlString);
            package.Ignore(p => p.ProjectUrlString);
            package.Ignore(p => p.RepositoryUrlString);

            package.Property(p => p.RowVersion).IsRowVersion();
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
