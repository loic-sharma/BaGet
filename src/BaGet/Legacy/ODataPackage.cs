using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BaGet.Core.Entities;

namespace BaGet.Legacy
{
    public class ODataPackage : IEquatable<ODataPackage>
    {
        public ODataPackage() { }

        public ODataPackage(Package package)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            if (package.Version == null)
                throw new ArgumentException("server package version is null");
            Version = package.Version.OriginalVersion;
            NormalizedVersion = package.Version.ToNormalizedString();

            Authors = package.Authors == null ? null : string.Join(",", package.Authors);
            IconUrl = package.IconUrl?.AbsoluteUri;
            LicenseUrl = package.LicenseUrl?.AbsoluteUri;
            ProjectUrl = package.ProjectUrl?.AbsoluteUri;
            Dependencies = ToDependenciesString(package.Dependencies);

            Id = package.Id;
            Title = package.Title;
            RequireLicenseAcceptance = package.RequireLicenseAcceptance;
            Description = package.Description;
            Summary = package.Summary;
            Language = package.Language;
            Tags = package.Tags == null ? null : string.Join(",", package.Tags);
            //PackageHashAlgorithm = package.PackageHashAlgorithm;
            //LastUpdated = package.LastUpdated.UtcDateTime;
            Published = package.Published;
            // IsAbsoluteLatestVersion = package.IsAbsoluteLatestVersion;
            // IsLatestVersion = package.IsLatestVersion;
            // IsPrerelease = !package.IsReleaseVersion();
            Listed = package.Listed;
            DownloadCount = (int)package.Downloads;
            if (package.MinClientVersion != null)
                MinClientVersion = package.MinClientVersion;

            //PackageSize = package.PackageSize;
            //Created = package.Created.UtcDateTime;
            //VersionDownloadCount = package.VersionDownloadCount;
        }

        public string Id { get; set; }

        public string Version { get; set; }

        public string NormalizedVersion { get; set; }

        public bool IsPrerelease { get; set; }

        public string Title { get; set; }

        public string Authors { get; set; }

        public string Owners { get; set; }

        public string IconUrl { get; set; }

        public string LicenseUrl { get; set; }

        public string ProjectUrl { get; set; }

        public int DownloadCount { get; set; }

        public bool RequireLicenseAcceptance { get; set; }

        public bool DevelopmentDependency { get; set; }

        public string Description { get; set; }

        public string Summary { get; set; }

        public string ReleaseNotes { get; set; }

        public DateTime Published { get; set; }

        public DateTime LastUpdated { get; set; }

        public string Dependencies { get; set; }

        public string PackageHash { get; set; }

        public string PackageHashAlgorithm { get; set; }

        public int PackageSize { get; set; }

        public string Copyright { get; set; }

        public string Tags { get; set; }

        public bool IsAbsoluteLatestVersion { get; set; }

        public bool IsLatestVersion { get; set; }

        public bool Listed { get; set; }

        public int VersionDownloadCount { get; set; }

        public string MinClientVersion { get; set; }

        public string Language { get; set; }

        public override string ToString()
        {
            return string.Format("{0} {1}", Id, Version);
        }

        public bool Equals(ODataPackage other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Id, other.Id) && Equals(Version, other.Version);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ODataPackage);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode() * 5432 + Version.GetHashCode() * 754;
        }

        private static string ToDependenciesString(IEnumerable<PackageDependency> dependencies)
        {
            if (dependencies == null || !dependencies.Any())
                return null;

            var texts = new List<string>();
            var frameworkDeps = dependencies.Where(IsFrameworkDependency).Select(d => d.TargetFramework).Distinct();
            foreach (var frameworkDep in frameworkDeps)
            {
                texts.Add(string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}", null, null, frameworkDep));
            }
            foreach (var packageDependency in dependencies.Where(d => !IsFrameworkDependency(d)))
            {
                texts.Add(string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}",
                    packageDependency.Id,
                    packageDependency.VersionRange == null ? null : packageDependency.VersionRange,
                    packageDependency.TargetFramework));
            }

            return string.Join("|", texts);
        }

        private static bool IsFrameworkDependency(PackageDependency dependency)
        {
            return dependency.Id == null && dependency.VersionRange == null;
        }
    }
}
